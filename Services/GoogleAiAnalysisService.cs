using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DeptDam.Data;
using DeptDam.Models;
using Microsoft.EntityFrameworkCore;

namespace DeptDam.Services;

/// <summary>
/// AI analysis using Google Gemini (gemini-1.5-flash) API.
/// Falls back gracefully to the simulated service if no API key is configured.
/// </summary>
public class GoogleAiAnalysisService : IAiAnalysisService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ApplicationDbContext _db;
    private readonly ITenantService _tenantService;
    private readonly ILogger<GoogleAiAnalysisService> _logger;

    public GoogleAiAnalysisService(
        IHttpClientFactory httpFactory,
        ApplicationDbContext db,
        ITenantService tenantService,
        ILogger<GoogleAiAnalysisService> logger)
    {
        _httpFactory = httpFactory;
        _db = db;
        _tenantService = tenantService;
        _logger = logger;
    }

    public async Task<AiAnalysisResult> AnalyzeAssetAsync(Stream imageStream, string originalFileName)
    {
        var settings = await GetSettingsAsync();

        if (string.IsNullOrWhiteSpace(settings?.GoogleAiApiKey))
        {
            _logger.LogWarning("Google AI API key not configured. Falling back to simulated analysis.");
            return await new SimulatedAiAnalysisService().AnalyzeAssetAsync(imageStream, originalFileName);
        }

        // Only image types are supported by Gemini Vision
        var ext = Path.GetExtension(originalFileName).ToLowerInvariant();
        var mimeType = ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => null
        };

        if (mimeType == null)
        {
            _logger.LogInformation("File type '{Ext}' not supported by Gemini Vision. Using simulated result.", ext);
            return await new SimulatedAiAnalysisService().AnalyzeAssetAsync(imageStream, originalFileName);
        }

        try
        {
            // Read into base64
            using var ms = new MemoryStream();
            await imageStream.CopyToAsync(ms);
            var base64 = Convert.ToBase64String(ms.ToArray());

            var model = settings.GeminiModel;
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={settings.GoogleAiApiKey}";

            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new
                            {
                                text = """
                                    Analyze this image and respond in valid JSON only (no markdown code fences).
                                    Return an object with these fields:
                                    - "tags": array of short descriptive strings (max 15) describing objects, scenes, colors, mood, style
                                    - "extractedText": string containing any visible text in the image, or null if none
                                    - "facesDetected": number of faces detected (0 if none)
                                    - "description": one-sentence description of the image
                                    """
                            },
                            new
                            {
                                inlineData = new { mimeType, data = base64 }
                            }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.2,
                    maxOutputTokens = 512,
                    responseMimeType = "application/json"
                }
            };

            var client = _httpFactory.CreateClient();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(url, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Gemini API error {Status}: {Body}", response.StatusCode, responseBody);
                return await new SimulatedAiAnalysisService().AnalyzeAssetAsync(imageStream, originalFileName);
            }

            return ParseGeminiResponse(responseBody);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to call Gemini API");
            return await new SimulatedAiAnalysisService().AnalyzeAssetAsync(imageStream, originalFileName);
        }
    }

    private AiAnalysisResult ParseGeminiResponse(string responseBody)
    {
        var result = new AiAnalysisResult();
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            // Navigate: candidates[0].content.parts[0].text
            var text = root
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();

            if (string.IsNullOrWhiteSpace(text)) return result;

            using var inner = JsonDocument.Parse(text);
            var innerRoot = inner.RootElement;

            if (innerRoot.TryGetProperty("tags", out var tagsEl))
                result.Tags = tagsEl.EnumerateArray().Select(t => t.GetString() ?? "").Where(t => t != "").ToList();

            if (innerRoot.TryGetProperty("extractedText", out var ocrEl) && ocrEl.ValueKind != JsonValueKind.Null)
                result.ExtractedText = ocrEl.GetString();

            if (innerRoot.TryGetProperty("facesDetected", out var facesEl))
            {
                var count = facesEl.ValueKind == JsonValueKind.Number ? facesEl.GetInt32() : 0;
                for (int i = 0; i < count; i++)
                    result.FacesDetected.Add($"Person {i + 1}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse Gemini response");
        }
        return result;
    }

    private async Task<AiSettings?> GetSettingsAsync()
    {
        var tenantId = _tenantService.GetCurrentTenantId();
        if (string.IsNullOrEmpty(tenantId)) return null;
        return await _db.AiSettings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId);
    }
}
