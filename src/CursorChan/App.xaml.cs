using System.Text;
using System.Windows;

namespace CursorChan;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // 日本語の zip / inf（Shift_JIS）を読むために必要
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "カーソルちゃん - エラー",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        base.OnStartup(e);
    }
}
