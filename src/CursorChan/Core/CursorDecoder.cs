using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CursorChan.Core;

/// <summary>カーソル 1 つ分の画像（.ani ならコマ送り情報つき）。</summary>
public sealed class CursorFrames
{
    public required BitmapSource[] Frames { get; init; }
    public required int[] Sequence { get; init; }
    public required int[] DurationsMs { get; init; }
    public int HotX { get; init; }
    public int HotY { get; init; }

    public bool IsAnimated => Sequence.Length > 1;
    public BitmapSource FrameAt(int step) => Frames[Sequence[step % Sequence.Length]];
    public int DurationAt(int step) => Math.Max(16, DurationsMs[step % DurationsMs.Length]);
}

/// <summary>.cur / .ani を WPF の画像に変換する。PNG から .cur を作る処理もここ。</summary>
public static class CursorDecoder
{
    private static readonly ConcurrentDictionary<string, (DateTime Stamp, CursorFrames? Frames)> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static CursorFrames? Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var full = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        if (!File.Exists(full)) return null;

        var stamp = File.GetLastWriteTimeUtc(full);
        if (Cache.TryGetValue(full, out var c) && c.Stamp == stamp) return c.Frames;

        CursorFrames? result;
        try { result = Decode(File.ReadAllBytes(full)); }
        catch { result = null; }
        result ??= LoadWithWin32(full);

        Cache[full] = (stamp, result);
        return result;
    }

    private static CursorFrames? Decode(byte[] d)
    {
        if (d.Length > 12 && Ascii(d, 0, 4) == "RIFF" && Ascii(d, 8, 4) == "ACON")
            return DecodeAni(d);

        var bmp = DecodeIco(d, 0, d.Length, out var hx, out var hy);
        return bmp == null ? null : new CursorFrames
        {
            Frames = [bmp], Sequence = [0], DurationsMs = [0], HotX = hx, HotY = hy,
        };
    }

    /// <summary>ICO / CUR 形式から一番大きい画像を取り出す。</summary>
    private static BitmapSource? DecodeIco(byte[] d, int off, int len, out int hotX, out int hotY)
    {
        hotX = hotY = 0;
        if (len < 6) return null;
        int type = U16(d, off + 2), count = U16(d, off + 4);
        if (type is not (1 or 2) || count == 0) return null;

        int best = -1, bestScore = -1;
        for (var i = 0; i < count; i++)
        {
            var e = off + 6 + 16 * i;
            if (e + 16 > off + len) break;
            int w = d[e] == 0 ? 256 : d[e];
            int size = I32(d, e + 8), o = I32(d, e + 12);
            if (o < 0 || size <= 0 || o + size > len) continue;
            var bpp = IsPng(d, off + o) ? 32 : U16(d, off + o + 14);
            var score = w * 100 + bpp;
            if (score > bestScore) { bestScore = score; best = i; }
        }
        if (best < 0) return null;

        var entry = off + 6 + 16 * best;
        if (type == 2) { hotX = U16(d, entry + 4); hotY = U16(d, entry + 6); }
        int imgSize = I32(d, entry + 8), imgOff = off + I32(d, entry + 12);

        BitmapFrame frame;
        if (IsPng(d, imgOff))
        {
            using var ms = new MemoryStream(d, imgOff, imgSize);
            frame = new PngBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        }
        else
        {
            // 1 枚だけのアイコン形式に組み直して WPF に読ませる
            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, Encoding.ASCII, true))
            {
                bw.Write((ushort)0); bw.Write((ushort)1); bw.Write((ushort)1);
                bw.Write(d[entry]); bw.Write(d[entry + 1]); bw.Write(d[entry + 2]); bw.Write((byte)0);
                bw.Write((ushort)1); bw.Write((ushort)U16(d, imgOff + 14));
                bw.Write(imgSize); bw.Write(22);
                bw.Write(d, imgOff, imgSize);
            }
            ms.Position = 0;
            frame = new IconBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        }
        frame.Freeze();
        return frame;
    }

    private static CursorFrames? DecodeAni(byte[] d)
    {
        int rate = 10, flags = 1;
        int[]? rates = null, seq = null;
        var icons = new List<(int Off, int Len)>();

        var end = Math.Min(d.Length, 8 + I32(d, 4));
        var p = 12;
        while (p + 8 <= end)
        {
            var id = Ascii(d, p, 4);
            var size = I32(d, p + 4);
            var body = p + 8;
            if (size < 0 || body + size > d.Length) break;
            switch (id)
            {
                case "anih" when size >= 36:
                    rate = I32(d, body + 28);
                    flags = I32(d, body + 32);
                    break;
                case "rate":
                    rates = ReadInts(d, body, size / 4);
                    break;
                case "seq ":
                    seq = ReadInts(d, body, size / 4);
                    break;
                case "LIST" when size >= 4 && Ascii(d, body, 4) == "fram":
                    var q = body + 4;
                    while (q + 8 <= body + size)
                    {
                        var sid = Ascii(d, q, 4);
                        var ssize = I32(d, q + 4);
                        if (ssize < 0 || q + 8 + ssize > d.Length) break;
                        if (sid == "icon") icons.Add((q + 8, ssize));
                        q += 8 + ssize + (ssize & 1);
                    }
                    break;
            }
            p = body + size + (size & 1);
        }

        if ((flags & 1) == 0 || icons.Count == 0) return null;

        var frames = new BitmapSource[icons.Count];
        int hx = 0, hy = 0;
        for (var i = 0; i < icons.Count; i++)
        {
            var f = DecodeIco(d, icons[i].Off, icons[i].Len, out var x, out var y);
            if (f == null) return null;
            if (i == 0) { hx = x; hy = y; }
            frames[i] = f;
        }

        var sequence = seq is { Length: > 0 } && seq.All(s => s >= 0 && s < frames.Length)
            ? seq
            : Enumerable.Range(0, frames.Length).ToArray();
        var jiffies = rates is { Length: > 0 } ? rates : [rate <= 0 ? 10 : rate];
        return new CursorFrames
        {
            Frames = frames,
            Sequence = sequence,
            DurationsMs = jiffies.Select(j => j * 1000 / 60).ToArray(),
            HotX = hx,
            HotY = hy,
        };
    }

    /// <summary>形式が特殊で読めなかったときは Windows 自身に読み込ませる（アニメは 1 コマ目のみ）。</summary>
    private static CursorFrames? LoadWithWin32(string path)
    {
        var h = Native.LoadImage(IntPtr.Zero, path, Native.IMAGE_CURSOR, 0, 0, Native.LR_LOADFROMFILE | Native.LR_DEFAULTSIZE);
        if (h == IntPtr.Zero) return null;
        try
        {
            var b = Imaging.CreateBitmapSourceFromHIcon(h, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            b.Freeze();
            return new CursorFrames { Frames = [b], Sequence = [0], DurationsMs = [0] };
        }
        catch
        {
            return null;
        }
        finally
        {
            Native.DestroyCursor(h);
        }
    }

    // ---------- 画像から .cur を作る ----------

    private static readonly int[] OutputSizes = [32, 48, 64, 96, 128, 256];

    /// <summary>
    /// 画像を複数サイズ入りの .cur にする。hotX / hotY は元画像のピクセル座標。
    /// Windows はカーソルのサイズ設定に合わせて近いサイズを選ぶ。
    /// </summary>
    public static void WriteCursor(BitmapSource src, double hotX, double hotY, string outPath)
    {
        var images = new List<(int Size, byte[] Data, int Hx, int Hy)>();
        double sw = src.PixelWidth, sh = src.PixelHeight;
        foreach (var size in OutputSizes)
        {
            var scale = size / Math.Max(sw, sh);
            var dv = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
            using (var dc = dv.RenderOpen())
                dc.DrawImage(src, new Rect(0, 0, sw * scale, sh * scale));
            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var bgra = new FormatConvertedBitmap(rtb, PixelFormats.Bgra32, null, 0);
            var hx = Math.Clamp((int)Math.Round(hotX * scale), 0, size - 1);
            var hy = Math.Clamp((int)Math.Round(hotY * scale), 0, size - 1);
            images.Add((size, ToDib(bgra, size), hx, hy));
        }

        using var fs = File.Create(outPath);
        using var bw = new BinaryWriter(fs);
        bw.Write((ushort)0); bw.Write((ushort)2); bw.Write((ushort)images.Count);
        var offset = 6 + 16 * images.Count;
        foreach (var img in images)
        {
            var b = (byte)(img.Size >= 256 ? 0 : img.Size);
            bw.Write(b); bw.Write(b); bw.Write((byte)0); bw.Write((byte)0);
            bw.Write((ushort)img.Hx); bw.Write((ushort)img.Hy);
            bw.Write(img.Data.Length); bw.Write(offset);
            offset += img.Data.Length;
        }
        foreach (var img in images) bw.Write(img.Data);
    }

    /// <summary>32bit BGRA ＋ 透過マスクの DIB（どの Windows でも読める昔ながらの形式）。</summary>
    private static byte[] ToDib(BitmapSource bgra, int size)
    {
        var stride = size * 4;
        var pixels = new byte[stride * size];
        bgra.CopyPixels(pixels, stride, 0);
        var maskStride = ((size + 31) / 32) * 4;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write(40); bw.Write(size); bw.Write(size * 2);
        bw.Write((ushort)1); bw.Write((ushort)32);
        bw.Write(0); bw.Write(stride * size + maskStride * size);
        bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
        for (var y = size - 1; y >= 0; y--) bw.Write(pixels, y * stride, stride);
        for (var y = size - 1; y >= 0; y--)
        {
            var row = new byte[maskStride];
            for (var x = 0; x < size; x++)
                if (pixels[y * stride + x * 4 + 3] == 0) row[x / 8] |= (byte)(0x80 >> (x % 8));
            bw.Write(row);
        }
        return ms.ToArray();
    }

    // ---------- 小物 ----------

    private static bool IsPng(byte[] d, int o) =>
        o + 8 <= d.Length && d[o] == 0x89 && d[o + 1] == 0x50 && d[o + 2] == 0x4E && d[o + 3] == 0x47;

    private static int U16(byte[] d, int o) => o + 2 <= d.Length ? BitConverter.ToUInt16(d, o) : 0;
    private static int I32(byte[] d, int o) => o + 4 <= d.Length ? BitConverter.ToInt32(d, o) : 0;
    private static string Ascii(byte[] d, int o, int n) => o + n <= d.Length ? Encoding.ASCII.GetString(d, o, n) : "";

    private static int[] ReadInts(byte[] d, int o, int n)
    {
        var r = new int[n];
        for (var i = 0; i < n; i++) r[i] = I32(d, o + i * 4);
        return r;
    }
}
