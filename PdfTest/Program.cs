using SkiaSharp;
using PDFLib;

// Create a test image
var imagePath = Path.Join(AppDomain.CurrentDomain.BaseDirectory, "test.png");
using (var bitmap = new SKBitmap(100, 100))
{
    using (var canvas = new SKCanvas(bitmap))
    {
        canvas.Clear(SKColors.Blue);
        using var paint = new SKPaint
        {
            Color = SKColors.Red,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1
        };
        canvas.DrawRect(10, 10, 80, 80, paint);

        paint.Color = SKColors.Green;
        paint.Style = SKPaintStyle.Fill;
        canvas.DrawOval(30, 30, 20, 20, paint); // In SkiaSharp, rx/ry are radii
    }
    
    using (var image = SKImage.FromBitmap(bitmap))
    using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
    using (var stream = File.OpenWrite(imagePath))
    {
        data.SaveTo(stream);
    }
}

using var doc = new PdfDocument();
using var fs = new FileStream(Path.Join(AppDomain.CurrentDomain.BaseDirectory, "streaming-test.pdf"), FileMode.Create);
doc.Begin(fs);

// Test Large number of pages
for (var i = 1; i <= 100; i++)
{
    var p = doc.AddPage();
    p.AddFont("F1", "Helvetica");
    p.DrawText("F1", 12, 50, 800, $"This is page {i} of 100");
    p.DrawLine(50, 790, 550, 790);
    
    if (i == 1)
    {
        // Add image only on first page to test it still works
        using var pdfImage = PdfImage.FromFile(imagePath);
        p.DrawImage("Img1", pdfImage, 50, 600, 100, 100);
    }
    
    p.Build(compress: true);
}

doc.Close();
Console.WriteLine("Streaming PDF saved to streaming-test.pdf");