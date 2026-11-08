namespace CursorShelf.Core;

public enum SchemeKind
{
    /// <summary>Windows 標準（既定に戻す先）</summary>
    WindowsDefault,
    /// <summary>Windows に最初から入っているスキーム（HKLM）</summary>
    System,
    /// <summary>コントロールパネルなどで登録されたスキーム（HKCU）</summary>
    User,
    /// <summary>このアプリのライブラリ（編集できる）</summary>
    Library,
}

public sealed class CursorScheme
{
    public string Name { get; set; } = "";

    /// <summary>レジストリ上の名前（表示名と違うことがある）。</summary>
    public string RegistryName { get; set; } = "";

    public SchemeKind Kind { get; set; }

    /// <summary>ライブラリのフォルダー（Library のときだけ）。</summary>
    public string? Folder { get; set; }

    /// <summary>役割名 → ファイルのパス（環境変数を含むことがある）。</summary>
    public Dictionary<string, string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsEditable => Kind == SchemeKind.Library;

    public string? GetPath(string role) =>
        Files.TryGetValue(role, out var p) && !string.IsNullOrWhiteSpace(p) ? p : null;

    /// <summary>適用したときに実際に使われるパス（未設定なら Windows 標準）。</summary>
    public string EffectivePath(CursorRole role) => GetPath(role.Name) ?? CursorRoles.DefaultPath(role);

    public int AssignedCount => CursorRoles.All.Count(r => GetPath(r.Name) != null);
}
