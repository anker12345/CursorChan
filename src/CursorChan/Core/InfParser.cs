using System.Text;
using System.Text.RegularExpressions;

namespace CursorChan.Core;

/// <summary>カーソル配布物によく入っている install.inf から、役割とファイルの対応を読む。</summary>
public static class InfParser
{
    public static (string Name, Dictionary<string, string> Files)? Parse(string infPath)
    {
        var dir = Path.GetDirectoryName(infPath)!;
        var lines = ReadText(infPath).Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith(';'))
            .ToList();

        // [Strings] を先に読む
        var strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? section = null;
        foreach (var line in lines)
        {
            if (line.StartsWith('[')) { section = line.Trim('[', ']').Trim(); continue; }
            if (!string.Equals(section, "Strings", StringComparison.OrdinalIgnoreCase)) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            strings[line[..eq].Trim()] = StripComment(line[(eq + 1)..]).Trim().Trim('"');
        }

        string Subst(string s) => Regex.Replace(s, "%([^%]+)%",
            m => strings.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);

        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? schemeName = null;
        var allFiles = new Lazy<string[]>(() => Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories));

        string? Resolve(string raw)
        {
            var name = Path.GetFileName(raw.Trim().Trim('"').Replace('/', '\\'));
            if (string.IsNullOrWhiteSpace(name)) return null;
            return allFiles.Value.FirstOrDefault(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var line in lines)
        {
            if (line.StartsWith('[')) continue;
            var f = SplitFields(line);
            if (f.Count < 5 || !f[0].StartsWith("HKCU", StringComparison.OrdinalIgnoreCase)) continue;
            var key = Subst(f[1]).TrimEnd('\\');

            if (key.EndsWith(@"Cursors\Schemes", StringComparison.OrdinalIgnoreCase))
            {
                // HKCU,"Control Panel\Cursors\Schemes","名前",,"a.cur,b.cur,..."
                schemeName ??= Subst(f[2]);
                var parts = Subst(f[4]).Split(',');
                for (var i = 0; i < parts.Length && i < CursorRoles.All.Count; i++)
                {
                    if (Resolve(Subst(parts[i])) is { } path)
                        files.TryAdd(CursorRoles.All[i].Name, path);
                }
            }
            else if (key.EndsWith(@"Control Panel\Cursors", StringComparison.OrdinalIgnoreCase)
                     && CursorRoles.Find(Subst(f[2])) is { } role)
            {
                // HKCU,"Control Panel\Cursors",Arrow,0x00020000,"%10%\Cursors\x\arrow.cur"
                if (Resolve(Subst(f[4])) is { } path) files.TryAdd(role.Name, path);
            }
        }

        if (files.Count == 0) return null;
        if (string.IsNullOrWhiteSpace(schemeName) || schemeName.Contains('%'))
            schemeName = strings.TryGetValue("SCHEME_NAME", out var sn) ? sn : Path.GetFileName(dir);
        return (schemeName, files);
    }

    private static string StripComment(string s)
    {
        var inQuote = false;
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '"') inQuote = !inQuote;
            else if (s[i] == ';' && !inQuote) return s[..i];
        }
        return s;
    }

    /// <summary>カンマ区切り（"" で囲まれた中のカンマは区切りにしない）。</summary>
    private static List<string> SplitFields(string line)
    {
        var list = new List<string>();
        var sb = new StringBuilder();
        var inQuote = false;
        foreach (var c in line)
        {
            if (c == '"') { inQuote = !inQuote; continue; }
            if (c == ';' && !inQuote) break;
            if (c == ',' && !inQuote) { list.Add(sb.ToString().Trim()); sb.Clear(); continue; }
            sb.Append(c);
        }
        list.Add(sb.ToString().Trim());
        return list;
    }

    /// <summary>UTF-8 / UTF-16 / Shift_JIS を判別して読む。</summary>
    public static string ReadText(string path)
    {
        var b = File.ReadAllBytes(path);
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return Encoding.Unicode.GetString(b, 2, b.Length - 2);
        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return Encoding.UTF8.GetString(b, 3, b.Length - 3);
        try { return new UTF8Encoding(false, true).GetString(b); }
        catch (DecoderFallbackException) { return Encoding.GetEncoding(932).GetString(b); }
    }
}
