using System.ComponentModel;
using System.Runtime.CompilerServices;
using CursorShelf.Core;

namespace CursorShelf;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>ギャラリーのカード 1 枚。</summary>
public sealed class SchemeItem(CursorScheme scheme) : Observable
{
    public CursorScheme Scheme { get; } = scheme;
    public string Name => Scheme.Name;

    public string KindLabel => Scheme.Kind switch
    {
        SchemeKind.WindowsDefault => "Windows 標準",
        SchemeKind.System => "Windows",
        SchemeKind.User => "登録済み",
        _ => "マイセット",
    };

    public string ArrowPath => Path("Arrow");
    public string HandPath => Path("Hand");
    public string HelpPath => Path("Help");
    public string WaitPath => Path("Wait");

    private string Path(string role) => Scheme.EffectivePath(CursorRoles.Find(role)!);

    private bool _isApplied;
    public bool IsApplied
    {
        get => _isApplied;
        set { if (_isApplied != value) { _isApplied = value; Raise(); } }
    }

    public void Refresh() => Raise(string.Empty);
}

/// <summary>詳細パネルの役割 1 行。</summary>
public sealed class RoleItem(CursorRole role, SchemeItem owner) : Observable
{
    public CursorRole Role { get; } = role;
    public SchemeItem Owner { get; } = owner;
    public string Label => Role.Label;
    public string PreviewPath => Owner.Scheme.EffectivePath(Role);
    public bool IsEditable => Owner.Scheme.IsEditable;

    public string FileName
    {
        get
        {
            var p = Owner.Scheme.GetPath(Role.Name);
            if (p == null) return Owner.Scheme.Kind == SchemeKind.WindowsDefault || CursorRoles.DefaultPath(Role).Length == 0
                ? "Windows 標準" : "未設定（Windows 標準）";
            var full = Environment.ExpandEnvironmentVariables(p);
            return File.Exists(full) ? System.IO.Path.GetFileName(full) : "ファイルが見つかりません";
        }
    }

    public void Refresh() => Raise(string.Empty);
}
