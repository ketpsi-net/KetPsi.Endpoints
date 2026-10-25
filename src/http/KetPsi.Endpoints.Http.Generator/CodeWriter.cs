using System.Text;

namespace KetPsi.Endpoints.Http.Generator;

/// <summary>Minimal indented text writer for generated code.</summary>
internal sealed class CodeWriter
{
    private readonly StringBuilder _sb = new();
    private int _indent;

    public CodeWriter(int indent = 0) => _indent = indent;

    public CodeWriter Line(string text = "")
    {
        if (text.Length == 0)
        {
            _sb.AppendLine();
            return this;
        }
        _sb.Append(' ', _indent * 4).AppendLine(text);
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

    public int Indent
    {
        get => _indent;
        set => _indent = value;
    }

    public void Raw(string text) => _sb.Append(text);

    public override string ToString() => _sb.ToString();
}
