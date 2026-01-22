namespace PDFLib.Chromium;

/// <summary>
/// Represents the visual data that should appear in the signature box
/// </summary>
/// <param name="Name"></param>
/// <param name="Date"></param>
public record Signature(string Name, string Date);