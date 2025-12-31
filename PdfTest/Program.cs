using System.Drawing;
using System.Drawing.Imaging;
using PDFLib;

// Create a test image
string imagePath = Path.Join(AppDomain.CurrentDomain.BaseDirectory, "test.png");
using (var bmp = new Bitmap(100, 100))
{
    using (var g = Graphics.FromImage(bmp))
    {
        g.Clear(Color.Blue);
        g.DrawRectangle(Pens.Red, 10, 10, 80, 80);
        g.FillEllipse(Brushes.Green, 30, 30, 40, 40);
    }
    bmp.Save(imagePath, ImageFormat.Png);
}

var doc = new PdfDocument();

var page = doc.AddPage();

page.AddFont("F1", "Helvetica");
page.AddFont("F2", "Times-Roman");

page.DrawText("F1", 24, 50, 780, "PDFLib Extended Features");
page.DrawText("F2", 12, 50, 760, "Testing Compression, Images, and Tables");

// Test Image
var pdfImage = PdfImage.FromFile(imagePath);
page.DrawImage("Img1", pdfImage, 50, 600, 100, 100);
page.DrawText("F1", 10, 50, 580, "Blue square with red border and green circle (100x100)");

// Test Table
var table = new PdfTable(new float[] { 100, 150, 100 });
table.AddRow("ID", "Name", "Role");
table.AddRow("1", "John Doe", "Developer");
table.AddRow("2", "Jane Smith", "Designer");
table.AddRow("3", "Bob Johnson", "Manager");
page.DrawTable(table, 50, 500);

page.DrawLine(50, 50, 500, 50);

// Build with compression
page.Build(compress: true);

doc.Save(Path.Join(AppDomain.CurrentDomain.BaseDirectory, "extended-test.pdf"));
Console.WriteLine("PDF saved to extended-test.pdf");