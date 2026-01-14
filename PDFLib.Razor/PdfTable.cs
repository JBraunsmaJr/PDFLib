namespace PDFLib;

/// <summary>
/// Provides a way to define and render tables within a <see cref="PdfPage"/>.
/// </summary>
public class PdfTable
{
    private readonly float[] _columnWidths;
    private readonly List<TableCellData> _rows = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfTable"/> class with specified column widths.
    /// </summary>
    /// <param name="columnWidths">An array of widths for each column.</param>
    public PdfTable(float[] columnWidths)
    {
        _columnWidths = columnWidths;
    }

    /// <summary>
    /// Gets or sets the font size for the table text.
    /// </summary>
    public int FontSize { get; set; } = 12;

    /// <summary>
    /// Gets or sets the font alias to use for the table text.
    /// </summary>
    public string FontAlias { get; set; } = "F1";

    /// <summary>
    /// Gets or sets the default row height. Rows will expand if text wraps.
    /// </summary>
    public int RowHeight { get; set; } = 20;

    /// <summary>
    /// Gets or sets the cell padding.
    /// </summary>
    public int Padding { get; set; } = 5;

    /// <summary>
    /// Adds a row of text cells to the table.
    /// </summary>
    /// <param name="cells">The text for each cell in the row.</param>
    public void AddRow(params string[] cells)
    {
        _rows.Add(new TableCellData
            { Texts = cells, BackgroundColors = new string?[cells.Length], TextColors = new string?[cells.Length] });
    }

    /// <summary>
    /// Adds a row with detailed cell data to the table.
    /// </summary>
    /// <param name="rowData">The cell data including text and colors.</param>
    public void AddRow(TableCellData rowData)
    {
        _rows.Add(rowData);
    }

    /// <summary>
    /// Renders the table onto the specified page at the given coordinates.
    /// </summary>
    /// <param name="page">The page to render the table on.</param>
    /// <param name="x">The X coordinate of the top-left corner.</param>
    /// <param name="y">The Y coordinate of the top-left corner.</param>
    /// <returns>The Y coordinate after the table has been rendered (the bottom of the table).</returns>
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
                var cellText = i < row.Texts.Length ? row.Texts[i] : "";
                var cellWidth = _columnWidths[i];
                var lines = TextMeasurer.WrapText(cellText, cellWidth - Padding * 2, FontSize);
                cellLines.Add(lines);

                var cellHeight = lines.Count * (FontSize + 2) + Padding * 2;
                if (cellHeight > rowMaxHeight) rowMaxHeight = cellHeight;
            }

            var currentX = x;
            for (var i = 0; i < _columnWidths.Length; i++)
            {
                var cellWidth = _columnWidths[i];
                var lines = cellLines[i];
                var bgColor = i < row.BackgroundColors.Length ? row.BackgroundColors[i] : null;
                var textColor = i < row.TextColors.Length ? row.TextColors[i] : null;

                if (bgColor != null)
                    page.DrawRectangle(currentX, currentY - rowMaxHeight, (int)cellWidth, rowMaxHeight, bgColor);

                // Draw cell border
                page.DrawLine(currentX, currentY, (int)(currentX + cellWidth), currentY); // Top
                page.DrawLine(currentX, currentY - rowMaxHeight, (int)(currentX + cellWidth),
                    currentY - rowMaxHeight); // Bottom
                page.DrawLine(currentX, currentY, currentX, currentY - rowMaxHeight); // Left
                page.DrawLine((int)(currentX + cellWidth), currentY, (int)(currentX + cellWidth),
                    currentY - rowMaxHeight); // Right

                // Draw text lines
                var textY = currentY - Padding;
                foreach (var line in lines)
                {
                    page.DrawText(FontAlias, FontSize, currentX + Padding, textY - FontSize, line, textColor);
                    textY -= FontSize + 2;
                }

                currentX += (int)cellWidth;
            }

            currentY -= rowMaxHeight;
        }

        return currentY;
    }

    /// <summary>
    /// Represents data for a single row in a <see cref="PdfTable"/>.
    /// </summary>
    public class TableCellData
    {
        /// <summary>
        /// Gets or sets the text content for each cell.
        /// </summary>
        public string[] Texts { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets the background colors for each cell (optional).
        /// </summary>
        public string?[] BackgroundColors { get; set; } = Array.Empty<string?>();

        /// <summary>
        /// Gets or sets the text colors for each cell (optional).
        /// </summary>
        public string?[] TextColors { get; set; } = Array.Empty<string?>();
    }
}