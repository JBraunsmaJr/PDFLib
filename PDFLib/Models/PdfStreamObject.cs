using System.Text;

namespace PDFLib.Models;

public class PdfStreamObject : PdfObject
{
    protected readonly PdfDictionary _dict;
    protected byte[] _content;

    public PdfStreamObject(PdfDictionary dict, string content) : this(dict, Encoding.ASCII.GetBytes(content))
    {
    }

    public PdfStreamObject(PdfDictionary dict, byte[] content)
    {
        _dict = dict;
        _content = content;
    }

    public override void WriteTo(BinaryWriter writer)
    {
        _dict.WriteTo(writer);
        writer.Write(ToAscii("\nstream\n"));
        writer.Write(_content);
        writer.Write(ToAscii("\nendstream"));
    }

    public override byte[] GetBytes()
    {
        var bytes = new List<byte>();
        bytes.AddRange(_dict.GetBytes());
        bytes.AddRange(ToAscii("\nstream\n"));
        bytes.AddRange(_content);
        bytes.AddRange(ToAscii("\nendstream"));
        return bytes.ToArray();
    }
}