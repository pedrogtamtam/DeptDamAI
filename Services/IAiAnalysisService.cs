namespace DeptDam.Services;

public interface IAiAnalysisService
{
    Task<AiAnalysisResult> AnalyzeAssetAsync(Stream imageStream, string originalFileName);
}

public class AiAnalysisResult
{
    public List<string> Tags { get; set; } = new();
    public string? ExtractedText { get; set; }
    public List<string> FacesDetected { get; set; } = new();
}
