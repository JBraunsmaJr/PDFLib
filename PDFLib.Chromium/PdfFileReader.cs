using System.Globalization;
using System.Text;

namespace PDFLib.Chromium;

internal enum PdfTokenKind
{
    Null,
    Boolean,
    Number,
    Name,
    String,
    HexString,
    Array,
    Dictionary,
    Reference,
    Keyword
}

/// <summary>
/// One PDF object as it appears in the file: its kind plus the exact bytes it occupies.
/// Keeping the original bytes lets the signer copy values it doesn't care about verbatim.
/// </summary>
internal readonly struct PdfToken
{
    private readonly ReadOnlyMemory<byte> _data;

    public PdfToken(ReadOnlyMemory<byte> data, PdfTokenKind kind, int start, int end)
    {
        _data = data;
        Kind = kind;
        Start = start;
        End = end;
    }

    public PdfTokenKind Kind { get; }

    /// <summary>Offset of the first byte of this object.</summary>
    public int Start { get; }

    /// <summary>Offset just past the last byte of this object.</summary>
    public int End { get; }

    public ReadOnlySpan<byte> Span => _data.Span[Start..End];

    public byte[] ToArray() => Span.ToArray();

    /// <summary>The object number when this is an indirect reference ("12 0 R").</summary>
    public bool TryGetReference(out int objectId)
    {
        objectId = 0;
        if (Kind != PdfTokenKind.Reference) return false;

        var span = Span;
        var i = 0;
        while (i < span.Length && span[i] is >= (byte)'0' and <= (byte)'9')
        {
            objectId = checked(objectId * 10 + (span[i] - '0'));
            i++;
        }

        return i > 0;
    }

    public bool TryGetNumber(out double value)
    {
        value = 0;
        return Kind == PdfTokenKind.Number &&
               double.TryParse(Encoding.ASCII.GetString(Span), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>The name including its leading slash, with #xx escapes decoded.</summary>
    public string? AsName()
    {
        return Kind == PdfTokenKind.Name ? PdfFileReader.DecodeName(Span) : null;
    }

    /// <summary>Decodes a literal or hexadecimal string to text (UTF-16BE when it carries a BOM, otherwise Latin-1).</summary>
    public string? AsText()
    {
        byte[] raw;
        switch (Kind)
        {
            case PdfTokenKind.String:
                raw = PdfFileReader.DecodeLiteralString(Span);
                break;
            case PdfTokenKind.HexString:
                raw = PdfFileReader.DecodeHexString(Span);
                break;
            default:
                return null;
        }

        if (raw.Length >= 2 && raw[0] == 0xFE && raw[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(raw, 2, raw.Length - 2);

        return Encoding.Latin1.GetString(raw);
    }
}

/// <summary>A parsed dictionary: keys in file order, values as raw tokens.</summary>
internal sealed class PdfDictionaryToken
{
    public List<(string Key, PdfToken Value)> Entries { get; } = new();

    public bool TryGet(string key, out PdfToken value)
    {
        foreach (var entry in Entries)
        {
            if (entry.Key != key) continue;
            value = entry.Value;
            return true;
        }

        value = default;
        return false;
    }

    public bool ContainsKey(string key) => TryGet(key, out _);
}

/// <summary>
/// Reads just enough of a PDF to sign it: the cross-reference chain (so the newest revision of every
/// object is found, including in files that already carry incremental updates) and individual objects.
/// </summary>
/// <remarks>
/// Supports classic cross-reference tables, which is what Chromium and this library write.
/// Files that use cross-reference streams (PDF 1.5 compressed xref) are rejected with a clear error.
/// </remarks>
internal sealed class PdfFileReader
{
    private readonly ReadOnlyMemory<byte> _data;
    private readonly Dictionary<int, int> _offsets = new();

    private PdfFileReader(ReadOnlyMemory<byte> data)
    {
        _data = data;
    }

    /// <summary>Offset of the newest cross-reference section; becomes /Prev of the next update.</summary>
    public long LastStartXref { get; private set; }

    /// <summary>One more than the highest object number in use.</summary>
    public int Size { get; private set; }

    /// <summary>Object number of the document catalog.</summary>
    public int RootId { get; private set; }

    /// <summary>The newest trailer; /Info and /ID are carried into each update.</summary>
    public PdfDictionaryToken Trailer { get; private set; } = new();

    public static PdfFileReader Parse(ReadOnlyMemory<byte> data)
    {
        var reader = new PdfFileReader(data);
        reader.ReadCrossReferences();
        return reader;
    }

    private void ReadCrossReferences()
    {
        var span = _data.Span;
        var marker = span.LastIndexOf("startxref"u8);
        if (marker < 0) throw new InvalidDataException("Not a valid PDF: 'startxref' not found.");

        var pos = marker + 9;
        if (!TryReadInteger(ref pos, out var startXref))
            throw new InvalidDataException("Not a valid PDF: 'startxref' is not followed by an offset.");

        LastStartXref = startXref;

        var visited = new HashSet<long>();
        var offset = startXref;
        var newest = true;

        while (visited.Add(offset))
        {
            if (offset < 0 || offset >= span.Length)
                throw new InvalidDataException($"Not a valid PDF: cross-reference offset {offset} is outside the file.");

            pos = SkipWhitespace((int)offset);
            if (!span[pos..].StartsWith("xref"u8))
                throw new NotSupportedException(
                    "This PDF uses cross-reference streams (compressed xref), which the signer does not support. " +
                    "PDFs produced by Chromium or by this library use classic cross-reference tables.");

            pos += 4;

            // Subsections: "first count" followed by count entries of "offset generation n|f"
            while (true)
            {
                pos = SkipWhitespace(pos);
                if (span[pos..].StartsWith("trailer"u8)) break;

                if (!TryReadInteger(ref pos, out var first) || !TryReadInteger(ref pos, out var count))
                    throw new InvalidDataException("Not a valid PDF: malformed cross-reference table.");

                for (var i = 0; i < count; i++)
                {
                    if (!TryReadInteger(ref pos, out var entryOffset) || !TryReadInteger(ref pos, out _))
                        throw new InvalidDataException("Not a valid PDF: malformed cross-reference entry.");

                    pos = SkipWhitespace(pos);
                    var type = span[pos++];
                    var id = (int)first + i;

                    // We walk from the newest section backwards, so the first entry seen for an object wins
                    if (_offsets.ContainsKey(id)) continue;
                    _offsets[id] = type == (byte)'n' ? (int)entryOffset : -1;
                }
            }

            pos += 7;
            var trailer = ReadDictionary(ReadToken(ref pos));

            if (newest)
            {
                Trailer = trailer;
                if (!trailer.TryGet("/Root", out var root) || !root.TryGetReference(out var rootId))
                    throw new InvalidDataException("Not a valid PDF: trailer has no /Root.");
                RootId = rootId;
                newest = false;
            }

            if (trailer.TryGet("/Size", out var sizeToken) && sizeToken.TryGetNumber(out var size))
                Size = Math.Max(Size, (int)size);

            if (trailer.ContainsKey("/XRefStm"))
                throw new NotSupportedException(
                    "This PDF is a hybrid-reference file (cross-reference streams), which the signer does not support.");

            if (!trailer.TryGet("/Prev", out var prev) || !prev.TryGetNumber(out var prevOffset)) break;
            offset = (long)prevOffset;
        }

        // Never hand out an object number that is already taken, even if /Size is wrong
        if (_offsets.Count > 0) Size = Math.Max(Size, _offsets.Keys.Max() + 1);
    }

    /// <summary>Returns the newest revision of an indirect object, or null if it doesn't exist.</summary>
    public PdfToken? GetObject(int objectId)
    {
        if (!_offsets.TryGetValue(objectId, out var offset) || offset < 0) return null;

        var pos = offset;
        if (!TryReadInteger(ref pos, out var id) || id != objectId || !TryReadInteger(ref pos, out _)) return null;

        pos = SkipWhitespace(pos);
        if (!_data.Span[pos..].StartsWith("obj"u8)) return null;
        pos += 3;

        return ReadToken(ref pos);
    }

    /// <summary>Follows indirect references until it reaches an actual value.</summary>
    public PdfToken? Resolve(PdfToken token)
    {
        for (var depth = 0; depth < 32; depth++)
        {
            if (!token.TryGetReference(out var id)) return token;
            var target = GetObject(id);
            if (target == null) return null;
            token = target.Value;
        }

        return null;
    }

    public PdfDictionaryToken? GetDictionary(int objectId)
    {
        var token = GetObject(objectId);
        return token is { Kind: PdfTokenKind.Dictionary } ? ReadDictionary(token.Value) : null;
    }

    /// <summary>Reads a dictionary that may be given inline or as a reference.</summary>
    public PdfDictionaryToken? ResolveDictionary(PdfToken token)
    {
        var resolved = Resolve(token);
        return resolved is { Kind: PdfTokenKind.Dictionary } ? ReadDictionary(resolved.Value) : null;
    }

    /// <summary>Reads an array that may be given inline or as a reference.</summary>
    public List<PdfToken> ResolveArray(PdfToken token)
    {
        var resolved = Resolve(token);
        return resolved is { Kind: PdfTokenKind.Array } ? ReadArray(resolved.Value) : new List<PdfToken>();
    }

    public PdfDictionaryToken ReadDictionary(PdfToken token)
    {
        if (token.Kind != PdfTokenKind.Dictionary) throw new InvalidDataException("Expected a PDF dictionary.");

        var result = new PdfDictionaryToken();
        var span = _data.Span;
        var pos = token.Start + 2;

        while (true)
        {
            pos = SkipWhitespace(pos);
            if (pos + 1 < token.End && span[pos] == (byte)'>' && span[pos + 1] == (byte)'>') break;
            if (pos >= token.End) break;

            var key = ReadToken(ref pos);
            if (key.Kind != PdfTokenKind.Name) throw new InvalidDataException("Malformed PDF dictionary: expected a name.");

            var value = ReadToken(ref pos);
            result.Entries.Add((key.AsName()!, value));
        }

        return result;
    }

    public List<PdfToken> ReadArray(PdfToken token)
    {
        if (token.Kind != PdfTokenKind.Array) throw new InvalidDataException("Expected a PDF array.");

        var result = new List<PdfToken>();
        var span = _data.Span;
        var pos = token.Start + 1;

        while (true)
        {
            pos = SkipWhitespace(pos);
            if (pos >= token.End || span[pos] == (byte)']') break;
            result.Add(ReadToken(ref pos));
        }

        return result;
    }

    /// <summary>
    /// Finds the object number of a page by its 1-based number, walking the page tree. Chromium nests
    /// pages under intermediate /Pages nodes for longer documents, so /Kids of the root is not a page list.
    /// </summary>
    public int? FindPage(int pageNumber)
    {
        var catalog = GetDictionary(RootId);
        if (catalog == null || !catalog.TryGet("/Pages", out var pages) || !pages.TryGetReference(out var pagesId)) return null;

        var remaining = pageNumber;
        return pageNumber < 1 ? null : FindPage(pagesId, ref remaining, 0);
    }

    private int? FindPage(int nodeId, ref int remaining, int depth)
    {
        if (depth > 64) return null;

        var node = GetDictionary(nodeId);
        if (node == null) return null;

        if (!node.TryGet("/Kids", out var kidsToken))
        {
            // A leaf, i.e. a page
            remaining--;
            return remaining == 0 ? nodeId : null;
        }

        // Skip whole subtrees that can't contain the page
        if (node.TryGet("/Count", out var countToken) && Resolve(countToken) is { } resolvedCount &&
            resolvedCount.TryGetNumber(out var count) && count < remaining && depth > 0)
        {
            remaining -= (int)count;
            return null;
        }

        foreach (var kid in ResolveArray(kidsToken))
        {
            if (!kid.TryGetReference(out var kidId)) continue;
            var found = FindPage(kidId, ref remaining, depth + 1);
            if (found != null) return found;
            if (remaining <= 0) return null;
        }

        return null;
    }

    // ---------------------------------------------------------------- tokenizer

    private PdfToken ReadToken(ref int pos)
    {
        var span = _data.Span;
        pos = SkipWhitespace(pos);
        if (pos >= span.Length) throw new InvalidDataException("Unexpected end of PDF data.");

        var start = pos;
        var b = span[pos];

        switch (b)
        {
            case (byte)'<' when pos + 1 < span.Length && span[pos + 1] == (byte)'<':
            {
                pos += 2;
                while (true)
                {
                    pos = SkipWhitespace(pos);
                    if (pos + 1 >= span.Length) throw new InvalidDataException("Unterminated PDF dictionary.");
                    if (span[pos] == (byte)'>' && span[pos + 1] == (byte)'>')
                    {
                        pos += 2;
                        return new PdfToken(_data, PdfTokenKind.Dictionary, start, pos);
                    }

                    ReadToken(ref pos);
                }
            }
            case (byte)'<':
            {
                var close = span[pos..].IndexOf((byte)'>');
                if (close < 0) throw new InvalidDataException("Unterminated PDF hex string.");
                pos += close + 1;
                return new PdfToken(_data, PdfTokenKind.HexString, start, pos);
            }
            case (byte)'[':
            {
                pos++;
                while (true)
                {
                    pos = SkipWhitespace(pos);
                    if (pos >= span.Length) throw new InvalidDataException("Unterminated PDF array.");
                    if (span[pos] == (byte)']')
                    {
                        pos++;
                        return new PdfToken(_data, PdfTokenKind.Array, start, pos);
                    }

                    ReadToken(ref pos);
                }
            }
            case (byte)'(':
            {
                var depth = 0;
                while (pos < span.Length)
                {
                    var c = span[pos++];
                    if (c == (byte)'\\') pos++;
                    else if (c == (byte)'(') depth++;
                    else if (c == (byte)')' && --depth == 0)
                        return new PdfToken(_data, PdfTokenKind.String, start, pos);
                }

                throw new InvalidDataException("Unterminated PDF string.");
            }
            case (byte)'/':
            {
                pos++;
                while (pos < span.Length && IsRegular(span[pos])) pos++;
                return new PdfToken(_data, PdfTokenKind.Name, start, pos);
            }
        }

        if (!IsRegular(b)) throw new InvalidDataException($"Unexpected character '{(char)b}' at offset {pos} in PDF.");

        while (pos < span.Length && IsRegular(span[pos])) pos++;
        var word = span[start..pos];

        if (IsNumber(word))
        {
            // "12 0 R" is a reference, not three separate objects
            if (IsUnsignedInteger(word))
            {
                var look = pos;
                if (TryReadInteger(ref look, out _))
                {
                    look = SkipWhitespace(look);
                    if (look < span.Length && span[look] == (byte)'R' &&
                        (look + 1 == span.Length || !IsRegular(span[look + 1])))
                    {
                        pos = look + 1;
                        return new PdfToken(_data, PdfTokenKind.Reference, start, pos);
                    }
                }
            }

            return new PdfToken(_data, PdfTokenKind.Number, start, pos);
        }

        if (word.SequenceEqual("true"u8) || word.SequenceEqual("false"u8))
            return new PdfToken(_data, PdfTokenKind.Boolean, start, pos);
        if (word.SequenceEqual("null"u8))
            return new PdfToken(_data, PdfTokenKind.Null, start, pos);

        return new PdfToken(_data, PdfTokenKind.Keyword, start, pos);
    }

    private bool TryReadInteger(ref int pos, out long value)
    {
        var span = _data.Span;
        var p = SkipWhitespace(pos);
        var start = p;
        value = 0;

        while (p < span.Length && span[p] is >= (byte)'0' and <= (byte)'9')
        {
            value = value * 10 + (span[p] - '0');
            p++;
            if (p - start > 18) return false;
        }

        if (p == start) return false;
        pos = p;
        return true;
    }

    private int SkipWhitespace(int pos)
    {
        var span = _data.Span;
        while (pos < span.Length)
        {
            var b = span[pos];
            if (IsWhitespace(b))
            {
                pos++;
            }
            else if (b == (byte)'%')
            {
                // Comment: runs to the end of the line
                while (pos < span.Length && span[pos] != (byte)'\n' && span[pos] != (byte)'\r') pos++;
            }
            else
            {
                break;
            }
        }

        return pos;
    }

    private static bool IsWhitespace(byte b) => b is 0 or 9 or 10 or 12 or 13 or 32;

    private static bool IsDelimiter(byte b) =>
        b is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}'
            or (byte)'/' or (byte)'%';

    private static bool IsRegular(byte b) => !IsWhitespace(b) && !IsDelimiter(b);

    private static bool IsUnsignedInteger(ReadOnlySpan<byte> word)
    {
        foreach (var b in word)
            if (b is < (byte)'0' or > (byte)'9')
                return false;
        return word.Length > 0;
    }

    private static bool IsNumber(ReadOnlySpan<byte> word)
    {
        var digits = 0;
        for (var i = 0; i < word.Length; i++)
        {
            var b = word[i];
            if (b is >= (byte)'0' and <= (byte)'9') digits++;
            else if (b == (byte)'.') continue;
            else if ((b == (byte)'+' || b == (byte)'-') && i == 0) continue;
            else return false;
        }

        return digits > 0;
    }

    internal static string DecodeName(ReadOnlySpan<byte> raw)
    {
        if (raw.IndexOf((byte)'#') < 0) return Encoding.Latin1.GetString(raw);

        var sb = new StringBuilder(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] == (byte)'#' && i + 2 < raw.Length && IsHex(raw[i + 1]) && IsHex(raw[i + 2]))
            {
                sb.Append((char)((HexValue(raw[i + 1]) << 4) | HexValue(raw[i + 2])));
                i += 2;
            }
            else
            {
                sb.Append((char)raw[i]);
            }
        }

        return sb.ToString();
    }

    internal static byte[] DecodeHexString(ReadOnlySpan<byte> raw)
    {
        // raw includes the surrounding < >
        var result = new List<byte>(raw.Length / 2);
        var high = -1;
        for (var i = 1; i < raw.Length - 1; i++)
        {
            if (!IsHex(raw[i])) continue;
            if (high < 0)
            {
                high = HexValue(raw[i]);
            }
            else
            {
                result.Add((byte)((high << 4) | HexValue(raw[i])));
                high = -1;
            }
        }

        if (high >= 0) result.Add((byte)(high << 4));
        return result.ToArray();
    }

    internal static byte[] DecodeLiteralString(ReadOnlySpan<byte> raw)
    {
        // raw includes the surrounding ( )
        var result = new List<byte>(raw.Length);
        for (var i = 1; i < raw.Length - 1; i++)
        {
            var b = raw[i];
            if (b != (byte)'\\')
            {
                result.Add(b);
                continue;
            }

            if (++i >= raw.Length - 1) break;
            var e = raw[i];
            switch (e)
            {
                case (byte)'n': result.Add(10); break;
                case (byte)'r': result.Add(13); break;
                case (byte)'t': result.Add(9); break;
                case (byte)'b': result.Add(8); break;
                case (byte)'f': result.Add(12); break;
                case (byte)'\r':
                    // Line continuation; swallow an optional following \n
                    if (i + 1 < raw.Length - 1 && raw[i + 1] == (byte)'\n') i++;
                    break;
                case (byte)'\n':
                    break;
                default:
                    if (e is >= (byte)'0' and <= (byte)'7')
                    {
                        var value = e - '0';
                        for (var n = 0; n < 2 && i + 1 < raw.Length - 1 && raw[i + 1] is >= (byte)'0' and <= (byte)'7'; n++)
                            value = (value << 3) | (raw[++i] - '0');
                        result.Add((byte)value);
                    }
                    else
                    {
                        result.Add(e);
                    }

                    break;
            }
        }

        return result.ToArray();
    }

    private static bool IsHex(byte b) =>
        b is >= (byte)'0' and <= (byte)'9' or >= (byte)'a' and <= (byte)'f' or >= (byte)'A' and <= (byte)'F';

    private static int HexValue(byte b) =>
        b <= (byte)'9' ? b - '0' : (b | 0x20) - 'a' + 10;
}
