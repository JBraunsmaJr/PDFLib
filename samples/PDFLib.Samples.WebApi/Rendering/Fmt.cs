using System.Globalization;

namespace PDFLib.Samples.WebApi.Rendering;

/// <summary>
/// Formatting helpers for report templates. Everything uses the invariant culture so the output is the
/// same on every machine, including containers that run with globalization-invariant mode.
/// </summary>
public static class Fmt
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Money(decimal value) =>
        (value < 0 ? "-$" : "$") + Math.Abs(value).ToString("#,##0.00", Inv);

    public static string MoneyWhole(decimal value) =>
        (value < 0 ? "-$" : "$") + Math.Abs(value).ToString("#,##0", Inv);

    /// <summary>$1.2M / $340K style, for chart axes and KPI tiles.</summary>
    public static string MoneyShort(decimal value) => Math.Abs(value) switch
    {
        >= 1_000_000 => "$" + (value / 1_000_000).ToString("0.0#", Inv) + "M",
        >= 1_000 => "$" + (value / 1_000).ToString("0", Inv) + "K",
        _ => "$" + value.ToString("0", Inv)
    };

    public static string Signed(decimal value) =>
        (value > 0 ? "+" : value < 0 ? "-" : "") + "$" + Math.Abs(value).ToString("#,##0.00", Inv);

    public static string Percent(decimal ratio, int decimals = 1) =>
        (ratio * 100).ToString("0." + new string('0', decimals), Inv) + "%";

    public static string SignedPercent(decimal ratio) =>
        (ratio > 0 ? "+" : "") + Percent(ratio);

    public static string Quantity(decimal value) => value.ToString("#,##0.##", Inv);

    public static string Number(int value) => value.ToString("#,##0", Inv);

    public static string Date(DateOnly date) => date.ToString("MMM d, yyyy", Inv);

    public static string ShortDate(DateOnly date) => date.ToString("MMM dd", Inv);

    public static string MonthYear(DateOnly date) => date.ToString("MMMM yyyy", Inv);

    /// <summary>A number for an SVG attribute: always a dot as the decimal separator.</summary>
    public static string Svg(double value) => value.ToString("0.##", Inv);
}
