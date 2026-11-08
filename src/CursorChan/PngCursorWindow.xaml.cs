using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CursorChan;

/// <summary>画像を選んでホットスポット（クリック位置）を決めるダイアログ。</summary>
public partial class PngCursorWindow : Window
{
    public BitmapSource Image { get; }
    public double HotX { get; private set; }
    public double HotY { get; private set; }

    public PngCursorWindow(string imagePath, string roleLabel)
    {
        InitializeComponent();
        TitleText.Text = $"「{roleLabel}」のカーソルを作る";

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(imagePath);
        bmp.EndInit();
        bmp.Freeze();
        Image = bmp;

        Preview.Source = bmp;
        // 小さい画像はドットがにじまないように拡大する
        if (Math.Max(bmp.PixelWidth, bmp.PixelHeight) <= 128)
            RenderOptions.SetBitmapScalingMode(Preview, BitmapScalingMode.NearestNeighbor);

        var scale = 318.0 / Math.Max(bmp.PixelWidth, bmp.PixelHeight);
        Preview.Width = bmp.PixelWidth * scale;
        Preview.Height = bmp.PixelHeight * scale;

        Loaded += (_, _) => UpdateMarker();
    }

    private void Stage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(Preview);
        HotX = Math.Clamp(p.X / Preview.Width * Image.PixelWidth, 0, Image.PixelWidth - 1);
        HotY = Math.Clamp(p.Y / Preview.Height * Image.PixelHeight, 0, Image.PixelHeight - 1);
        UpdateMarker();
    }

    private void UpdateMarker()
    {
        var x = HotX / Image.PixelWidth * Preview.Width;
        var y = HotY / Image.PixelHeight * Preview.Height;
        LineH.X1 = x - 10; LineH.X2 = x + 10; LineH.Y1 = LineH.Y2 = y;
        LineV.Y1 = y - 10; LineV.Y2 = y + 10; LineV.X1 = LineV.X2 = x;
        Canvas.SetLeft(Dot, x - 4);
        Canvas.SetTop(Dot, y - 4);
        HotText.Text = $"クリック位置：{(int)HotX}, {(int)HotY}（画像 {Image.PixelWidth} × {Image.PixelHeight} px）";
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
