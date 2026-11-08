using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace CursorChan.Core;

public sealed class ImportResult
{
    public List<CursorScheme> Imported { get; } = [];
    public List<string> Messages { get; } = [];
}

/// <summary>zip / フォルダー / install.inf / .cur・.ani からカーソルセットを取り込む。</summary>
public static class SchemeImporter
{
    private static readonly string[] CursorExts = [".cur", ".ani"];

    public static ImportResult Import(IEnumerable<string> paths, IEnumerable<string> existingNames)
    {
        var result = new ImportResult();
        var names = new List<string>(existingNames);
        var loose = new List<string>();

        foreach (var path in paths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    ImportFolder(path, Path.GetFileName(path.TrimEnd('\\')), result, names);
                    continue;
                }
                switch (Path.GetExtension(path).ToLowerInvariant())
                {
                    case ".zip":
                        ImportZip(path, result, names);
                        break;
                    case ".inf":
                        if (!ImportInf(path, result, names))
                            ImportFolder(Path.GetDirectoryName(path)!, null, result, names);
                        break;
                    case ".cur" or ".ani":
                        loose.Add(path);
                        break;
                    default:
                        result.Messages.Add($"{Path.GetFileName(path)}: 対応していない形式です");
                        break;
                }
            }
            catch (Exception ex)
            {
                result.Messages.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        if (loose.Count > 0)
            CreateFromFiles("取り込んだカーソル", loose, result, names);

        return result;
    }

    private static void ImportZip(string zip, ImportResult result, List<string> names)
    {
        var temp = Path.Combine(Path.GetTempPath(), "CursorChan-" + Guid.NewGuid().ToString("N"));
        try
        {
            // UTF-8 フラグのない日本語のファイル名は Shift_JIS として読む
            ZipFile.ExtractToDirectory(zip, temp, Encoding.GetEncoding(932));
            var before = result.Imported.Count;
            ImportFolder(temp, Path.GetFileNameWithoutExtension(zip), result, names);
            if (result.Imported.Count == before)
                result.Messages.Add($"{Path.GetFileName(zip)}: カーソルが見つかりませんでした");
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch { /* 一時フォルダーなので放置してよい */ }
        }
    }

    private static void ImportFolder(string dir, string? nameHint, ImportResult result, List<string> names)
    {
        var any = false;
        foreach (var inf in Directory.EnumerateFiles(dir, "*.inf", SearchOption.AllDirectories))
            any |= ImportInf(inf, result, names);
        if (any) return;

        // inf がないときはフォルダーごとにファイル名から役割を推測する
        var groups = Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
            .Where(f => CursorExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .GroupBy(Path.GetDirectoryName);
        foreach (var g in groups)
        {
            var groupDir = g.Key ?? dir;
            var same = string.Equals(Path.GetFullPath(groupDir).TrimEnd('\\'), Path.GetFullPath(dir).TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase);
            var name = same && nameHint != null ? nameHint : Path.GetFileName(groupDir);
            CreateFromFiles(name, g.ToList(), result, names);
        }
    }

    private static bool ImportInf(string inf, ImportResult result, List<string> names)
    {
        var parsed = InfParser.Parse(inf);
        if (parsed == null || parsed.Value.Files.Count == 0) return false;
        var (name, files) = parsed.Value;
        Create(name, files, result, names);
        return true;
    }

    private static void CreateFromFiles(string name, List<string> files, ImportResult result, List<string> names)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var unknown = new List<string>();
        foreach (var f in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var role = GuessRole(Path.GetFileNameWithoutExtension(f));
            if (role != null && !map.ContainsKey(role)) map[role] = f;
            else unknown.Add(f);
        }
        if (!map.ContainsKey("Arrow") && unknown.Count > 0)
        {
            map["Arrow"] = unknown[0];
            unknown.RemoveAt(0);
        }
        if (unknown.Count > 0)
        {
            var list = string.Join(", ", unknown.Take(8).Select(Path.GetFileName));
            if (unknown.Count > 8) list += $" ほか {unknown.Count - 8} 個";
            result.Messages.Add($"「{name}」: 役割がわからないファイルがありました（{list}）。あとから個別に設定できます。");
        }
        Create(name, map, result, names);
    }

    private static void Create(string name, Dictionary<string, string> files, ImportResult result, List<string> names)
    {
        var unique = SchemeLibrary.UniqueName(string.IsNullOrWhiteSpace(name) ? "カーソルセット" : name.Trim(), names);
        var scheme = SchemeLibrary.CreateNew(unique);
        foreach (var (role, file) in files)
        {
            if (File.Exists(file) && CursorDecoder.Load(file) != null)
                SchemeLibrary.SetRoleFile(scheme, role, file);
        }
        names.Add(unique);
        result.Imported.Add(scheme);
    }

    // ファイル名 → 役割。短い語はファイル名の区切りと完全一致、長い語は部分一致で判定する。
    private static readonly (string Role, string[] Contains, string[] Tokens)[] Rules =
    [
        ("NWPen", ["handwriting", "手書き"], ["pen"]),
        ("Person", ["person", "人の選択"], []),
        ("Pin", ["location", "場所"], ["pin", "loc"]),
        ("Hand", ["link", "hand", "リンク"], []),
        ("Help", ["help", "ヘルプ"], []),
        ("AppStarting", ["working", "appstarting", "background", "バックグラウンド"], []),
        ("Wait", ["busy", "wait", "loading", "待ち"], []),
        ("Crosshair", ["cross", "precision", "領域"], []),
        ("IBeam", ["text", "ibeam", "beam", "テキスト"], []),
        ("No", ["unavail", "forbid", "利用不可"], ["no"]),
        ("SizeNS", ["vert", "上下"], ["ns", "sizens"]),
        ("SizeWE", ["horz", "horiz", "左右"], ["ew", "we", "sizewe"]),
        ("SizeNWSE", ["dgn1", "diag1", "diagonal1", "nwse", "斜め1", "斜め１"], []),
        ("SizeNESW", ["dgn2", "diag2", "diagonal2", "nesw", "斜め2", "斜め２"], []),
        ("SizeAll", ["move", "sizeall", "移動"], []),
        ("UpArrow", ["alternate", "uparrow", "代替"], ["alt", "up"]),
        ("Arrow", ["arrow", "normal", "pointer", "default", "通常"], []),
    ];

    public static string? GuessRole(string fileName)
    {
        var lower = fileName.ToLowerInvariant();
        var tokens = Regex.Split(lower, @"[^a-z0-9]+").Where(t => t.Length > 0).ToHashSet();
        foreach (var (role, contains, toks) in Rules)
        {
            if (contains.Any(lower.Contains) || toks.Any(tokens.Contains))
                return role;
        }
        return null;
    }
}
