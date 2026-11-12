using System.Text;

namespace KetPsi.Endpoints.Grpc.Generator;

internal sealed class CodeWriter
{
    private readonly StringBuilder _sb = new();
    private int _indent;

    public CodeWriter Line(string text = "")
    {
        if (text.Length == 0)
        {
            _sb.Append('\n');
            return this;
        }
        _sb.Append(' ', _indent * 4).Append(text).Append('\n');
        return this;
    }

    public CodeWriter Open(string? header = null)
    {
        if (header is not null)
            Line(header);
        Line("{");
        _indent++;
        return this;
    }

    public CodeWriter Close(string suffix = "")
    {
        _indent--;
        Line("}" + suffix);
        return this;
    }

    public override string ToString() => _sb.ToString();
}
