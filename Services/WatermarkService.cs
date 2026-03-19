using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.Fonts;

namespace DeptDam.Services;

public interface IWatermarkService
{
    Task<Stream> ApplyWatermarkAsync(Stream inputStream, string text, string contentType);
}

public class WatermarkService : IWatermarkService
{
    public async Task<Stream> ApplyWatermarkAsync(Stream inputStream, string text, string contentType)
    {
        using var image = await Image.LoadAsync(inputStream);
        
        // Simple watermark: diagonal text
        var font = SystemFonts.CreateFont("Arial", image.Width / 10);
        
        image.Mutate(x => {
            x.DrawText(
                new RichTextOptions(font) {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Origin = new PointF(image.Width / 2, image.Height / 2)
                },
                text,
                Color.White.WithAlpha(0.3f)
            );
        });

        var outputStream = new MemoryStream();
        if (contentType == "image/png")
            await image.SaveAsPngAsync(outputStream);
        else
            await image.SaveAsJpegAsync(outputStream);
            
        outputStream.Position = 0;
        return outputStream;
    }
}
