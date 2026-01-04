namespace PDFApi.Models;

public class PdfJobRequest
{
    public required Guid JobId { get; set; }
    public required string HtmlContent { get; set; }
}

public record PdfJobQueueRequest(Guid JobId, string FilePath);