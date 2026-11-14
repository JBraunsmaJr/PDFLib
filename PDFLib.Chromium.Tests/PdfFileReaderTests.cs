using System.Text;

namespace PDFLib.Chromium.Tests;

public class PdfFileReaderTests
{
    private static byte[] Pdf(string trailerExtra, params string[] objects)
    {
        var sb = new StringBuilder("%PDF-1.4\n%âãÏÓ\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(sb.Length);
            sb.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = sb.Length;
        sb.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) sb.Append($"{offset:D10} 00000 n \n");
        sb.Append($"trailer\n<</Size {objects.Length + 1} /Root 1 0 R {trailerExtra}>>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    [Fact]
    public void ReadsTrailer_AndObjects()
    {
        var reader = PdfFileReader.Parse(Pdf("/Info 2 0 R", "<</Type /Catalog>>", "<</Title (x)>>"));

        Assert.Equal(1, reader.RootId);
        Assert.Equal(3, reader.Size);
        Assert.True(reader.Trailer.ContainsKey("/Info"));
        Assert.Equal("/Catalog", reader.GetDictionary(1)!.Entries.Single().Value.AsName());
        Assert.Null(reader.GetObject(99));
    }

    [Fact]
    public void Dictionary_KeepsEveryValueByteForByte_WhateverItsShape()
    {
        const string tricky =
            "<< /Name /Va#6Cue /Ref 12 0 R /Num -3.5 /Nums [1 0 R 2 3 4 0 R] " +
            "/Str (a \\) b (nested (deep)) \\\\) /Hex <48 65 6C6C6F> /Nested << /A << /B [ << /C (>>) >> ] >> >> " +
            "/Bool true /Nothing null % a comment with >> in it\n /Last 7 >>";

        var reader = PdfFileReader.Parse(Pdf("", tricky));
        var dict = reader.GetDictionary(1)!;

        Assert.Equal(
            ["/Name", "/Ref", "/Num", "/Nums", "/Str", "/Hex", "/Nested", "/Bool", "/Nothing", "/Last"],
            dict.Entries.Select(e => e.Key));

        string Raw(string key) => dict.TryGet(key, out var v) ? Encoding.Latin1.GetString(v.Span) : "?";

        Assert.Equal("/Value", dict.Entries[0].Value.AsName());
        Assert.Equal("12 0 R", Raw("/Ref"));
        Assert.Equal("-3.5", Raw("/Num"));
        Assert.Equal("(a \\) b (nested (deep)) \\\\)", Raw("/Str"));
        Assert.Equal("<< /A << /B [ << /C (>>) >> ] >> >>", Raw("/Nested"));
        Assert.Equal("7", Raw("/Last"));

        Assert.True(dict.TryGet("/Ref", out var reference) && reference.TryGetReference(out var id) && id == 12);
        Assert.True(dict.TryGet("/Str", out var str));
        Assert.Equal("a ) b (nested (deep)) \\", str.AsText());
        Assert.True(dict.TryGet("/Hex", out var hex));
        Assert.Equal("Hello", hex.AsText());

        // "1 0 R 2 3 4 0 R" is reference, number, number, reference
        Assert.True(dict.TryGet("/Nums", out var nums));
        Assert.Equal(
            [PdfTokenKind.Reference, PdfTokenKind.Number, PdfTokenKind.Number, PdfTokenKind.Reference],
            reader.ReadArray(nums).Select(t => t.Kind));
    }

    [Fact]
    public void Text_DecodesUtf16_AndOctalEscapes()
    {
        var reader = PdfFileReader.Parse(Pdf("", "<< /U <FEFF00480069> /O (\\101\\102\\n) >>"));
        var dict = reader.GetDictionary(1)!;

        Assert.True(dict.TryGet("/U", out var utf16));
        Assert.Equal("Hi", utf16.AsText());
        Assert.True(dict.TryGet("/O", out var octal));
        Assert.Equal("AB\n", octal.AsText());
    }

    [Fact]
    public void IncrementalUpdate_NewestRevisionOfAnObjectWins()
    {
        var original = Pdf("", "<</Type /Catalog /Version 1>>", "<</Keep true>>");
        var text = Encoding.Latin1.GetString(original);
        var prev = int.Parse(text[(text.LastIndexOf("startxref", StringComparison.Ordinal) + 10)..].Split('\n')[0]);

        var sb = new StringBuilder(text);
        var offset1 = sb.Length;
        sb.Append("1 0 obj\n<</Type /Catalog /Version 2>>\nendobj\n");
        var offset3 = sb.Length;
        sb.Append("3 0 obj\n<</New true>>\nendobj\n");
        var xref = sb.Length;
        sb.Append($"xref\n1 1\n{offset1:D10} 00000 n \n3 1\n{offset3:D10} 00000 n \n");
        sb.Append($"trailer\n<</Size 4 /Root 1 0 R /Prev {prev}>>\nstartxref\n{xref}\n%%EOF\n");

        var reader = PdfFileReader.Parse(Encoding.Latin1.GetBytes(sb.ToString()));

        Assert.Equal(4, reader.Size);
        Assert.Equal(xref, reader.LastStartXref);
        Assert.True(reader.GetDictionary(1)!.TryGet("/Version", out var version) && version.TryGetNumber(out var v) && v == 2);
        Assert.NotNull(reader.GetDictionary(2)); // only in the original revision
        Assert.NotNull(reader.GetDictionary(3)); // only in the update
    }

    [Fact]
    public void FindPage_WalksNestedPageTrees()
    {
        var reader = PdfFileReader.Parse(Pdf("",
            "<</Type /Catalog /Pages 2 0 R>>",
            "<</Type /Pages /Count 5 /Kids [3 0 R 6 0 R 9 0 R]>>",
            "<</Type /Pages /Count 2 /Kids [4 0 R 5 0 R]>>",
            "<</Type /Page>>",
            "<</Type /Page>>",
            "<</Type /Pages /Count 2 /Kids [7 0 R 8 0 R]>>",
            "<</Type /Page>>",
            "<</Type /Page>>",
            "<</Type /Page>>"));

        Assert.Equal([4, 5, 7, 8, 9], Enumerable.Range(1, 5).Select(n => reader.FindPage(n)));
        Assert.Null(reader.FindPage(6));
        Assert.Null(reader.FindPage(0));
    }

    [Fact]
    public void GarbageInput_FailsCleanly()
    {
        Assert.Throws<InvalidDataException>(() => PdfFileReader.Parse(Encoding.ASCII.GetBytes("not a pdf")));
        Assert.Throws<InvalidDataException>(() => PdfFileReader.Parse(Encoding.ASCII.GetBytes("%PDF-1.4\nstartxref\n999999\n%%EOF")));
    }
}
