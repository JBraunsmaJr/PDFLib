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

    public int Render(PdfPage page, int x, int y)
    {
        var currentY = y;

        foreach (var row in _rows)
        {
            var rowMaxHeight = RowHeight;
            var cellLines = new List<List<string>>();

            // Prepare wrapped text for each cell and find max row height
            for (var i = 0; i < _columnWidths.Length; i++)
            {
                var cellText = i < row.Length ? row[i] : "";
                var cellWidth = _columnWidths[i];
                var lines = TextMeasurer.WrapText(cellText, cellWidth - (Padding * 2), FontSize);
                cellLines.Add(lines);
                
                var cellHeight = lines.Count * (FontSize + 2) + (Padding * 2);
                if (cellHeight > rowMaxHeight) rowMaxHeight = (int)cellHeight;
            }

            var currentX = x;
            for (var i = 0; i < _columnWidths.Length; i++)
            {
                var cellWidth = _columnWidths[i];
                var lines = cellLines[i];

                // Draw cell border
                page.DrawLine(currentX, currentY, (int)(currentX + cellWidth), currentY); // Top
                page.DrawLine(currentX, currentY - rowMaxHeight, (int)(currentX + cellWidth), currentY - rowMaxHeight); // Bottom
                page.DrawLine(currentX, currentY, currentX, currentY - rowMaxHeight); // Left
                page.DrawLine((int)(currentX + cellWidth), currentY, (int)(currentX + cellWidth), currentY - rowMaxHeight); // Right

                // Draw text lines
                var textY = currentY - Padding;
                foreach (var line in lines)
                {
                    page.DrawText(FontAlias, FontSize, currentX + Padding, textY - FontSize, line);
                    textY -= (FontSize + 2);
                }

                currentX += (int)cellWidth;
            }
            currentY -= rowMaxHeight;
        }

        return currentY;
    }
}