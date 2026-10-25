using System.Text.RegularExpressions;

namespace KetPsi.Endpoints.Grpc.Tests;

/// <summary>Minimal structural parser for the flat proto3 files KetPsi emits (validation in tests).</summary>
internal static class ProtoText
{
    internal sealed class ProtoFile
    {
        public string Package { get; set; } = "";
        public HashSet<string> DefinedTypes { get; } = new(StringComparer.Ordinal);
        public HashSet<string> ReferencedTypes { get; } = new(StringComparer.Ordinal);
        public List<string> FirstEnumValues { get; } = new();
        /// <summary>"message X" / "enum X" → normalized body lines (comments stripped).</summary>
        public Dictionary<string, string> Blocks { get; } = new(StringComparer.Ordinal);
    }

    private static readonly HashSet<string> Scalars = new(StringComparer.Ordinal)
    {
        "double", "float", "int32", "int64", "uint32", "uint64", "sint32", "sint64", "fixed32", "fixed64",
        "sfixed32", "sfixed64", "bool", "string", "bytes",
    };

    public static Dictionary<string, string> SplitBundle(string bundle)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!bundle.StartsWith("// ===== file: ", StringComparison.Ordinal))
        {
            result[Parse(bundle).Package + ".proto"] = bundle;
            return result;
        }
        var parts = Regex.Split(bundle, "^// ===== file: (.+?) =====\n", RegexOptions.Multiline);
        for (var i = 1; i + 1 < parts.Length; i += 2)
            result[parts[i]] = parts[i + 1].TrimEnd('\n') + "\n";
        return result;
    }

    public static ProtoFile Parse(string text)
    {
        var file = new ProtoFile();
        string? block = null;
        string? kind = null;
        var body = new List<string>();
        var firstEnumValueSeen = false;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw;
            var comment = line.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0)
                line = line.Substring(0, comment);
            line = line.Trim();
            if (line.Length == 0)
                continue;

            if (block is null)
            {
                var m = Regex.Match(line, @"^package\s+([\w.]+);$");
                if (m.Success)
                {
                    file.Package = m.Groups[1].Value;
                    continue;
                }
                m = Regex.Match(line, @"^(message|enum|service)\s+(\w+)\s*\{$");
                if (m.Success)
                {
                    kind = m.Groups[1].Value;
                    block = kind + " " + m.Groups[2].Value;
                    if (kind != "service")
                        file.DefinedTypes.Add(m.Groups[2].Value);
                    body.Clear();
                    firstEnumValueSeen = false;
                }
                continue;
            }

            if (line == "}")
            {
                if (kind != "service")
                    file.Blocks[block] = string.Join("\n", body);
                block = null;
                continue;
            }

            body.Add(Regex.Replace(line, @"\s+", " "));
            if (kind == "enum")
            {
                var ev = Regex.Match(line, @"^\w+\s*=\s*(-?\d+);$");
                if (ev.Success && !firstEnumValueSeen)
                {
                    file.FirstEnumValues.Add(ev.Groups[1].Value);
                    firstEnumValueSeen = true;
                }
            }
            else if (kind == "message")
            {
                var f = Regex.Match(line, @"^(?:repeated\s+|optional\s+)?(map<\s*\w+\s*,\s*([\w.]+)\s*>|[\w.]+)\s+\w+\s*=\s*\d+;$");
                if (!f.Success)
                    throw new FormatException("Unparseable field line: " + line);
                var type = f.Groups[2].Success && f.Groups[2].Value.Length > 0 ? f.Groups[2].Value : f.Groups[1].Value;
                if (!Scalars.Contains(type))
                    file.ReferencedTypes.Add(type);
            }
            else if (kind == "service")
            {
                var r = Regex.Match(line, @"^rpc\s+\w+\s*\(\s*(?:stream\s+)?([\w.]+)\s*\)\s*returns\s*\(\s*(?:stream\s+)?([\w.]+)\s*\);$");
                if (!r.Success)
                    throw new FormatException("Unparseable rpc line: " + line);
                file.ReferencedTypes.Add(r.Groups[1].Value);
                file.ReferencedTypes.Add(r.Groups[2].Value);
            }
        }
        if (block is not null)
            throw new FormatException("Unterminated block " + block);
        return file;
    }
}
