using System.Globalization;

namespace PDFLib.Models;

/// <summary>
/// Represents a PDF number object (integer or real).
/// </summary>
public class PdfNumber : PdfObject
{
    private readonly string _value;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfNumber"/> class with an integer value.
    /// </summary>
    /// <param name="value">The integer value.</param>
    public PdfNumber(int value)
    {
        _value = value.ToString();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfNumber"/> class with a double value.
    /// </summary>
    /// <param name="value">The double value.</param>
    public PdfNumber(double value)
    {
        _value = value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfNumber"/> class with a string representation of a number.
    /// </summary>
    /// <param name="value">The number as a string.</param>
    public PdfNumber(string value)
    {
        _value = value;
    }

    /// <summary>
    /// Returns the raw byte representation of the number.
    /// </summary>
    /// <returns>A byte array representing the number.</returns>
    public override byte[] GetBytes()
    {
        return ToAscii(_value);
    }
}