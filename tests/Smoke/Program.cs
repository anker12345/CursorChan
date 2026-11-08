// 動作確認用の簡易テスト。使い方: dotnet run --project tests/Smoke -- [カーソルのフォルダー] [--apply]
// --apply を付けると実際にカーソルを切り替えて、最後に元へ戻す。
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows.Media.Imaging;
using CursorChan.Core;
using Microsoft.Win32;

namespace Smoke;

internal static class Program
{
    private static int _fail;

    [STAThread]
    private static int Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cursorDir = args.FirstOrDefault(a => !a.StartsWith("--")) ?? Environment.ExpandEnvironmentVariables(@"%SystemRoot%\Cursors");
        var doApply = args.Contains("--apply");
        var temp = Path.Combine(Path.GetTempPath(), "CursorChan-smoke-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(temp);
        var created = new List<CursorScheme>();

        try
        {
            // 1. Windows 標準カーソルの読み込み
            foreach (var role in CursorRoles.All.Where(r => r.DefaultFile.Length > 0))
            {
                var f = CursorDecoder.Load(CursorRoles.DefaultPath(role));
                Check(f != null, $"標準カーソル {role.Name} を読める");
            }
            var busy = CursorDecoder.Load(@"%SystemRoot%\cursors\aero_busy.ani");
            Check(busy is { IsAnimated: true }, $"aero_busy.ani がアニメとして読める（{busy?.Frames.Length} コマ）");

            // 2. ファイル名からの役割推測（inf なしの zip）
            var files = Directory.GetFiles(cursorDir).Where(f => f.EndsWith(".ani") || f.EndsWith(".cur")).ToList();
            var guessed = files.Select(f => SchemeImporter.GuessRole(Path.GetFileNameWithoutExtension(f))).ToList();
            Console.WriteLine("  推測: " + string.Join(", ", files.Zip(guessed, (f, g) => $"{Path.GetFileName(f)}→{g ?? "?"}")));
            var zipDir = Path.Combine(temp, "zipsrc", "テストセット");
            Directory.CreateDirectory(zipDir);
            foreach (var f in files) File.Copy(f, Path.Combine(zipDir, Path.GetFileName(f)));
            var zip = Path.Combine(temp, "テスト.zip");
            ZipFile.CreateFromDirectory(Path.Combine(temp, "zipsrc"), zip);
            var r1 = SchemeImporter.Import([zip], []);
            created.AddRange(r1.Imported);
            Check(r1.Imported.Count == 1, $"zip（inf なし）から 1 セット取り込み: {r1.Imported.FirstOrDefault()?.Name} {r1.Imported.FirstOrDefault()?.AssignedCount} 種類");
            foreach (var m in r1.Messages) Console.WriteLine("  メッセージ: " + m);

            // 3. Shift_JIS の install.inf
            var infDir = Path.Combine(temp, "infset");
            Directory.CreateDirectory(infDir);
            File.Copy(CursorRoles.DefaultPath(CursorRoles.All[0]).Replace("%SystemRoot%", Environment.GetEnvironmentVariable("SystemRoot")), Path.Combine(infDir, "矢印.cur"));
            File.Copy(Environment.ExpandEnvironmentVariables(@"%SystemRoot%\cursors\aero_busy.ani"), Path.Combine(infDir, "待ち.ani"));
            var inf = """
                [Version]
                signature="$CHICAGO$"
                [DefaultInstall]
                AddReg = Scheme.Reg
                [Scheme.Reg]
                HKCU,"Control Panel\Cursors\Schemes","%SCHEME_NAME%",,"%10%\%CUR_DIR%\%pointer%,,,%10%\%CUR_DIR%\%busy%"
                [Strings]
                CUR_DIR = "Cursors\テスト"
                SCHEME_NAME = "日本語のセット"
                pointer = "矢印.cur"
                busy = "待ち.ani"
                """;
            File.WriteAllText(Path.Combine(infDir, "install.inf"), inf, Encoding.GetEncoding(932));
            var r2 = SchemeImporter.Import([Path.Combine(infDir, "install.inf")], r1.Imported.Select(s => s.Name));
            created.AddRange(r2.Imported);
            var s2 = r2.Imported.FirstOrDefault();
            Check(s2 is { Name: "日本語のセット", AssignedCount: 2 } && s2.GetPath("Arrow") != null && s2.GetPath("Wait") != null,
                $"Shift_JIS の inf を読める: {s2?.Name} {s2?.AssignedCount} 種類");

            // 4. 画像から .cur を作る
            var png = Path.Combine(AppContext.BaseDirectory, "app.png");
            if (File.Exists(png))
            {
                var bmp = new BitmapImage(new Uri(png));
                var cur = Path.Combine(temp, "made.cur");
                CursorDecoder.WriteCursor(bmp, 100, 50, cur);
                var made = CursorDecoder.Load(cur);
                Check(made != null && made.Frames[0].PixelWidth == 256, $"画像から .cur を作れる（{new FileInfo(cur).Length / 1024} KB, ホットスポット {made?.HotX},{made?.HotY}）");
                var h = Native_LoadCursor(cur);
                Check(h, "作った .cur を Windows が読み込める");
            }

            // 5. ライブラリの保存と再読み込み
            var reloaded = SchemeLibrary.Load().FirstOrDefault(s => s.Name == s2?.Name);
            Check(reloaded?.AssignedCount == 2, "ライブラリを読み直しても設定が残る");

            // 6. 実際に適用して戻す
            if (doApply && r1.Imported.FirstOrDefault() is { } s1)
            {
                var backup = Path.Combine(temp, "backup.reg");
                RunReg($"export \"HKCU\\Control Panel\\Cursors\" \"{backup}\" /y");
                try
                {
                    SystemCursors.Apply(s1);
                    using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors")!;
                    Check((string?)key.GetValue("") == s1.Name, "適用するとレジストリのスキーム名が変わる");
                    Check(((string?)key.GetValue("Arrow"))?.StartsWith(s1.Folder!) == true, "Arrow がライブラリのファイルを指す");
                    Check(SystemCursors.CurrentSchemeName() == s1.Name, "CurrentSchemeName が一致");
                    Thread.Sleep(1500);
                    SystemCursors.Apply(SystemCursors.WindowsDefault());
                    Check(SystemCursors.CurrentSchemeName() == "Windows Default", "既定に戻せる");
                }
                finally
                {
                    RunReg($"import \"{backup}\"");
                    foreach (var s in created) SystemCursors.UnregisterUserScheme(s.Name);
                    SystemCursors.Reload();
                    Console.WriteLine("  元のカーソル設定に戻しました");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("例外: " + ex);
            _fail++;
        }
        finally
        {
            foreach (var s in created) SchemeLibrary.Delete(s);
            try { Directory.Delete(temp, true); } catch { }
        }

        Console.WriteLine(_fail == 0 ? "すべて OK" : $"{_fail} 件失敗");
        return _fail == 0 ? 0 : 1;
    }

    private static void Check(bool ok, string what)
    {
        Console.WriteLine($"{(ok ? "OK  " : "NG  ")}{what}");
        if (!ok) _fail++;
    }

    private static bool Native_LoadCursor(string path)
    {
        var h = LoadImage(IntPtr.Zero, path, 2, 0, 0, 0x10 | 0x40);
        if (h == IntPtr.Zero) return false;
        DestroyCursor(h);
        return true;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr h, string n, uint t, int cx, int cy, uint f);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyCursor(IntPtr h);

    private static void RunReg(string args)
    {
        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("reg.exe", args)
        { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true })!;
        p.WaitForExit();
    }
}
