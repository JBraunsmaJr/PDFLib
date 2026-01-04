namespace PDFApi.Models;

public class PdfJobResult
{
    public required bool Success { get; set; }
    public required string FilePath { get; set; }
    public required byte[] Key { get; set; }
    public required byte[] Iv;
    public string? ErrorMessage { get; set; }
}