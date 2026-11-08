namespace CursorShelf.Core;

/// <summary>Windows のカーソルの役割（レジストリの値の名前と表示名）。</summary>
public sealed record CursorRole(string Name, string Label, string DefaultFile);

public static class CursorRoles
{
    // 並び順は Windows の「スキーム」の文字列と同じ順番（変えないこと）
    public static readonly IReadOnlyList<CursorRole> All =
    [
        new("Arrow", "通常の選択", "aero_arrow.cur"),
        new("Help", "ヘルプの選択", "aero_helpsel.cur"),
        new("AppStarting", "バックグラウンドで作業中", "aero_working.ani"),
        new("Wait", "待ち状態", "aero_busy.ani"),
        new("Crosshair", "領域選択", ""),
        new("IBeam", "テキスト選択", ""),
        new("NWPen", "手書き", "aero_pen.cur"),
        new("No", "利用不可", "aero_unavail.cur"),
        new("SizeNS", "上下に拡大/縮小", "aero_ns.cur"),
        new("SizeWE", "左右に拡大/縮小", "aero_ew.cur"),
        new("SizeNWSE", "斜めに拡大/縮小 1", "aero_nwse.cur"),
        new("SizeNESW", "斜めに拡大/縮小 2", "aero_nesw.cur"),
        new("SizeAll", "移動", "aero_move.cur"),
        new("UpArrow", "代替選択", "aero_up.cur"),
        new("Hand", "リンクの選択", "aero_link.cur"),
        new("Pin", "場所の選択", "aero_pin.cur"),
        new("Person", "人の選択", "aero_person.cur"),
    ];

    public static CursorRole? Find(string? name) =>
        name == null ? null : All.FirstOrDefault(r => r.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Windows 標準のファイル。空文字は「Windows 内蔵のカーソル」を意味する。</summary>
    public static string DefaultPath(CursorRole role) =>
        role.DefaultFile.Length == 0 ? "" : @"%SystemRoot%\cursors\" + role.DefaultFile;
}
