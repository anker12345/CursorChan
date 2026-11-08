using Microsoft.Win32;

namespace CursorChan.Core;

/// <summary>Windows のカーソル設定（HKCU\Control Panel\Cursors）の読み書き。管理者権限は不要。</summary>
public static class SystemCursors
{
    private const string CursorsKey = @"Control Panel\Cursors";
    private const string UserSchemesKey = @"Control Panel\Cursors\Schemes";
    private const string SystemSchemesKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\Cursors\Schemes";
    public const string WindowsDefaultRegistryName = "Windows Default";

    public static string? CurrentSchemeName()
    {
        using var key = Registry.CurrentUser.OpenSubKey(CursorsKey);
        return key?.GetValue("") as string;
    }

    public static CursorScheme WindowsDefault()
    {
        var s = new CursorScheme
        {
            Name = "Windows 標準",
            RegistryName = WindowsDefaultRegistryName,
            Kind = SchemeKind.WindowsDefault,
        };
        foreach (var role in CursorRoles.All)
            s.Files[role.Name] = CursorRoles.DefaultPath(role);
        return s;
    }

    /// <summary>Windows に入っているスキーム（HKLM）と、登録済みスキーム（HKCU）。</summary>
    public static List<CursorScheme> LoadInstalledSchemes()
    {
        var list = new List<CursorScheme>();
        Read(Registry.LocalMachine, SystemSchemesKey, SchemeKind.System);
        Read(Registry.CurrentUser, UserSchemesKey, SchemeKind.User);
        return list;

        void Read(RegistryKey root, string path, SchemeKind kind)
        {
            using var key = root.OpenSubKey(path);
            if (key == null) return;
            foreach (var name in key.GetValueNames())
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string data) continue;
                var display = Native.ResolveIndirect(name);
                if (display.Equals(WindowsDefaultRegistryName, StringComparison.OrdinalIgnoreCase)) continue;
                var scheme = new CursorScheme { Name = display, RegistryName = name, Kind = kind };
                var parts = data.Split(',');
                for (var i = 0; i < parts.Length && i < CursorRoles.All.Count; i++)
                {
                    var p = parts[i].Trim();
                    if (p.Length > 0) scheme.Files[CursorRoles.All[i].Name] = p;
                }
                list.Add(scheme);
            }
        }
    }

    /// <summary>スキームを今のカーソルとして適用する。</summary>
    public static void Apply(CursorScheme scheme)
    {
        var (regName, source) = scheme.Kind switch
        {
            SchemeKind.WindowsDefault => (WindowsDefaultRegistryName, 2),
            SchemeKind.System => (scheme.RegistryName, 2),
            SchemeKind.User => (scheme.RegistryName, 1),
            _ => (scheme.Name, 1),
        };

        if (scheme.Kind == SchemeKind.Library)
            RegisterUserScheme(scheme);

        using (var key = Registry.CurrentUser.CreateSubKey(CursorsKey, true))
        {
            key.SetValue("", regName, RegistryValueKind.String);
            foreach (var role in CursorRoles.All)
                key.SetValue(role.Name, scheme.EffectivePath(role), RegistryValueKind.ExpandString);
            key.SetValue("Scheme Source", source, RegistryValueKind.DWord);
        }
        Reload();
    }

    /// <summary>コントロールパネルの「マウスのプロパティ」からも選べるように登録する。</summary>
    public static void RegisterUserScheme(CursorScheme scheme)
    {
        using var key = Registry.CurrentUser.CreateSubKey(UserSchemesKey, true);
        var data = string.Join(",", CursorRoles.All.Select(scheme.EffectivePath));
        key.SetValue(scheme.Name, data, RegistryValueKind.ExpandString);
    }

    public static void UnregisterUserScheme(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(UserSchemesKey, true);
        key?.DeleteValue(name, false);
    }

    public static int GetBaseSize()
    {
        using var key = Registry.CurrentUser.OpenSubKey(CursorsKey);
        return key?.GetValue("CursorBaseSize") is int v && v >= 32 ? v : 32;
    }

    public static void SetBaseSize(int size)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(CursorsKey, true))
            key.SetValue("CursorBaseSize", Math.Clamp(size, 32, 256), RegistryValueKind.DWord);
        Reload();
    }

    /// <summary>レジストリの内容を Windows に読み込み直させる（再起動不要で反映）。</summary>
    public static void Reload() =>
        Native.SystemParametersInfo(Native.SPI_SETCURSORS, 0, IntPtr.Zero,
            Native.SPIF_UPDATEINIFILE | Native.SPIF_SENDCHANGE);
}
