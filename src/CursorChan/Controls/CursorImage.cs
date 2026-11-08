using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CursorChan.Core;

namespace CursorChan.Controls;

/// <summary>.cur / .ani を表示する Image。アニメーションカーソルは動かして見せる。</summary>
public sealed class CursorImage : Image
{
    public static readonly DependencyProperty FilePathProperty = DependencyProperty.Register(
        nameof(FilePath), typeof(string), typeof(CursorImage),
        new PropertyMetadata(null, (d, _) => ((CursorImage)d).Reload()));

    public string? FilePath
    {
        get => (string?)GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render);
    private CursorFrames? _frames;
    private int _step;

    public CursorImage()
    {
        Stretch = Stretch.Uniform;
        StretchDirection = StretchDirection.DownOnly;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        _timer.Tick += OnTick;
        Loaded += (_, _) => StartIfAnimated();
        Unloaded += (_, _) => _timer.Stop();
    }

    private void Reload()
    {
        _timer.Stop();
        _frames = CursorDecoder.Load(FilePath);
        _step = 0;
        Source = _frames?.FrameAt(0);
        StartIfAnimated();
    }

    private void StartIfAnimated()
    {
        if (_frames is not { IsAnimated: true } || !IsLoaded) return;
        _timer.Interval = TimeSpan.FromMilliseconds(_frames.DurationAt(_step));
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_frames == null) { _timer.Stop(); return; }
        _step = (_step + 1) % _frames.Sequence.Length;
        Source = _frames.FrameAt(_step);
        _timer.Interval = TimeSpan.FromMilliseconds(_frames.DurationAt(_step));
    }
}
