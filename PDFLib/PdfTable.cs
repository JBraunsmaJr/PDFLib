namespace PDFLib;

public class PdfTable
{
    private readonly List<string[]> _rows = new();
    private readonly float[] _columnWidths;
    public int FontSize { get; set; } = 12;
    public string FontAlias { get; set; } = "F1";
    public int RowHeight { get; set; } = 20;
    public int Padding { get; set; } = 5;

    public PdfTable(float[] columnWidths)
    {
        _columnWidths = columnWidths;
    }

    public void AddRow(params string[] cells)
    {
        _rows.Add(cells);
    }

    public void Render(PdfPage page, int x, int y)
    {
        int currentY = y;
        float totalWidth = _columnWidths.Sum();

        foreach (var row in _rows)
        {
            int currentX = x;
            for (int i = 0; i < _columnWidths.Length; i++)
            {
                var cellText = i < row.Length ? row[i] : "";
                var cellWidth = _columnWidths[i];

                // Draw cell border
                page.DrawLine(currentX, currentY, (int)(currentX + cellWidth), currentY); // Top
                page.DrawLine(currentX, currentY - RowHeight, (int)(currentX + cellWidth), currentY - RowHeight); // Bottom
                page.DrawLine(currentX, currentY, currentX, currentY - RowHeight); // Left
                page.DrawLine((int)(currentX + cellWidth), currentY, (int)(currentX + cellWidth), currentY - RowHeight); // Right

                // Draw text
                page.DrawText(FontAlias, FontSize, currentX + Padding, currentY - RowHeight + Padding, cellText);

                currentX += (int)cellWidth;
            }
            currentY -= RowHeight;
        }
    }
}