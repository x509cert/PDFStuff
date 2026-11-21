using System.Windows;

namespace Broadside.Reader;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        using var window = new MainWindow(args.Length > 0 ? args[0] : null);
        return app.Run(window);
    }
}
