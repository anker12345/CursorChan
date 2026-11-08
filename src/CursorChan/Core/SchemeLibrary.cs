using System.Text.Json;

namespace CursorChan.Core;

/// <summary>アプリのライブラリ（%LOCALAPPDATA%\CursorChan\Library）にあるカーソルセット。</summary>
public static class SchemeLibrary
{
    private const string ManifestName = "scheme.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CursorChan", "Library");

    private sealed class Manifest
    {
        public string Name { get; set; } = "";
        public Dictionary<string, string> Files { get; set; } = new();
        public DateTime Created { get; set; }
    }

    public static List<CursorScheme> Load()
    {
        var list = new List<CursorScheme>();
        if (!Directory.Exists(Root)) return list;
        foreach (var dir in Directory.EnumerateDirectories(Root))
        {
            var file = Path.Combine(dir, ManifestName);
            if (!File.Exists(file)) continue;
            try
            {
                var m = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(file), JsonOptions);
                if (m == null) continue;
                var s = new CursorScheme { Name = m.Name, RegistryName = m.Name, Kind = SchemeKind.Library, Folder = dir };
                foreach (var (role, rel) in m.Files)
                {
                    if (CursorRoles.Find(role) is not { } r || string.IsNullOrWhiteSpace(rel)) continue;
                    s.Files[r.Name] = Path.IsPathRooted(rel) ? rel : Path.Combine(dir, rel);
                }
                list.Add(s);
            }
            catch
            {
                // 壊れた scheme.json は無視
            }
        }
        return list;
    }

    public static void Save(CursorScheme s)
    {
        if (s.Folder == null) throw new InvalidOperationException("ライブラリのセットではありません。");
        Directory.CreateDirectory(s.Folder);
        var file = Path.Combine(s.Folder, ManifestName);
        var created = DateTime.Now;
        if (File.Exists(file))
        {
            try { created = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(file))?.Created ?? created; }
            catch { /* 作り直す */ }
        }
        var m = new Manifest { Name = s.Name, Created = created };
        foreach (var (role, path) in s.Files)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            m.Files[role] = IsInside(path, s.Folder) ? Path.GetRelativePath(s.Folder, path) : path;
        }
        File.WriteAllText(file, JsonSerializer.Serialize(m, JsonOptions));
    }

    public static CursorScheme CreateNew(string name)
    {
        var folder = Path.Combine(Root, $"{SafeFileName(name)}-{Guid.NewGuid().ToString("N")[..8]}");
        var s = new CursorScheme { Name = name, RegistryName = name, Kind = SchemeKind.Library, Folder = folder };
        Save(s);
        return s;
    }

    /// <summary>どの種類のセットでも、ファイルごとライブラリにコピーして編集できるようにする。</summary>
    public static CursorScheme Duplicate(CursorScheme source, string name)
    {
        var s = CreateNew(name);
        foreach (var role in CursorRoles.All)
        {
            var src = source.GetPath(role.Name);
            if (src == null) continue;
            var full = Environment.ExpandEnvironmentVariables(src);
            if (File.Exists(full)) CopyIn(s, role.Name, full);
        }
        Save(s);
        return s;
    }

    public static void Delete(CursorScheme s)
    {
        if (s.Folder != null && IsInside(s.Folder, Root) && Directory.Exists(s.Folder))
            Directory.Delete(s.Folder, true);
    }

    /// <summary>役割のファイルを差し替える（ファイルはセットのフォルダーにコピーされる）。</summary>
    public static void SetRoleFile(CursorScheme s, string role, string sourceFile)
    {
        var old = s.GetPath(role);
        CopyIn(s, role, sourceFile);
        RemoveIfUnused(s, old);
        Save(s);
    }

    public static void ClearRole(CursorScheme s, string role)
    {
        var old = s.GetPath(role);
        s.Files.Remove(role);
        RemoveIfUnused(s, old);
        Save(s);
    }

    private static void CopyIn(CursorScheme s, string role, string sourceFile)
    {
        var ext = Path.GetExtension(sourceFile).ToLowerInvariant();
        if (ext is not (".cur" or ".ani")) ext = ".cur";
        var dest = Path.Combine(s.Folder!, $"{role}-{DateTime.Now:yyyyMMddHHmmssfff}{ext}");
        File.Copy(sourceFile, dest, true);
        s.Files[role] = dest;
    }

    private static void RemoveIfUnused(CursorScheme s, string? path)
    {
        if (path == null || s.Folder == null || !IsInside(path, s.Folder)) return;
        if (s.Files.Values.Any(v => string.Equals(v, path, StringComparison.OrdinalIgnoreCase))) return;
        try { File.Delete(path); } catch { /* 使用中なら残す */ }
    }

    public static string UniqueName(string baseName, IEnumerable<string> existing)
    {
        var set = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        if (!set.Contains(baseName)) return baseName;
        for (var i = 2; ; i++)
        {
            var n = $"{baseName} ({i})";
            if (!set.Contains(n)) return n;
        }
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var s = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        if (s.Length > 40) s = s[..40];
        return s.Length == 0 ? "set" : s;
    }

    private static bool IsInside(string path, string folder)
    {
        var p = Path.GetFullPath(path);
        var f = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
        return p.StartsWith(f, StringComparison.OrdinalIgnoreCase);
    }
}
