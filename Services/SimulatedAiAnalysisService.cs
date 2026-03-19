namespace DeptDam.Services;

public class SimulatedAiAnalysisService : IAiAnalysisService
{
    public async Task<AiAnalysisResult> AnalyzeAssetAsync(Stream imageStream, string originalFileName)
    {
        // Simulate network delay for calling Azure/AWS/GCP AI APIs
        await Task.Delay(1500);

        var result = new AiAnalysisResult();
        var loweredName = originalFileName.ToLowerInvariant();

        // 1. Simulated Object/Scene Recognition (Auto-Tagging)
        if (loweredName.Contains("mountain") || loweredName.Contains("landscape"))
        {
            result.Tags.AddRange(new[] { "nature", "landscape", "outdoors", "scenic" });
        }
        if (loweredName.Contains("city") || loweredName.Contains("cyberpunk"))
        {
            result.Tags.AddRange(new[] { "urban", "architecture", "cityscape", "night", "neon" });
        }
        if (loweredName.Contains("product"))
        {
            result.Tags.AddRange(new[] { "ecommerce", "studio", "merchandise" });
        }

        // Add some generic tags simulation
        if (!result.Tags.Any())
        {
            result.Tags.AddRange(new[] { "photography", "asset" });
        }

        // 2. Simulated Optical Character Recognition (OCR) / Document Analysis
        if (loweredName.Contains("document") || loweredName.Contains("invoice") || loweredName.EndsWith(".pdf"))
        {
            result.ExtractedText = "INVOICE #10293\nTotal: $1,250.00\nPayment Due: Net 30\nNotes: Thank you for your business.";
            result.Tags.Add("document");
            result.Tags.Add("finance");
        }
        else if (loweredName.Contains("sign") || loweredName.Contains("neon"))
        {
            // Simulate reading text from an image
            result.ExtractedText = "CAFE OPEN 24 HOURS";
        }

        // 3. Simulated Facial Recognition
        if (loweredName.Contains("headshot") || loweredName.Contains("team") || loweredName.Contains("person"))
        {
            result.FacesDetected.Add("John Doe");
            result.Tags.AddRange(new[] { "people", "portrait", "staff" });
        }
        else if (loweredName.Contains("event"))
        {
            result.FacesDetected.AddRange(new[] { "Jane Smith", "CEO Group" });
            result.Tags.Add("event");
        }

        return result;
    }
}
