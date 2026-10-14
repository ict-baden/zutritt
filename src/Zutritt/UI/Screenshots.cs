using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zutritt.Interop;
using Zutritt.UI.Theme;

namespace Zutritt.UI;

/// <summary>
/// <c>Zutritt.exe --screenshots &lt;ordner&gt;</c>: lädt die Demo, bringt jede Seite in einen festen Zustand und speichert
/// sie als PNG (144 dpi). Das Fenster liegt außerhalb des Bildschirms und wird nie aktiviert – das funktioniert auch
/// ohne sichtbaren Desktop. Mica gibt es in Bildern nicht, deshalb normaler Hintergrund aus dem Theme.
/// </summary>
internal static class Screenshots
{
    private const double Scale = 1.5; // 144 dpi
    private const double Width = 1200;
    private const double Height = 800;

    public static async Task<int> RunAsync(string folder, string theme)
    {
        Directory.CreateDirectory(folder);
        bool[] variants = theme switch
        {
            "hell" => [false],
            "dunkel" => [true],
            _ => [false, true],
        };
        Console.WriteLine($"Zutritt {AppInfo.Version} – Screenshots nach {folder}");

        using var state = AppState.OpenDemo(strict: true);
        int written = 0;
        foreach (var dark in variants)
        {
            var window = CreateWindow(state, dark);
            try
            {
                foreach (var slug in MainWindow.PageSlugs)
                {
                    await window.PrepareScreenshotAsync(slug);
                    // Auf- und Zuklapp-Animationen (Baum, Fluent) zu Ende laufen lassen
                    await Task.Delay(400);
                    await Ui.IdleAsync();
                    var file = Path.Combine(folder, $"{slug}-{(dark ? "dunkel" : "hell")}.png");
                    Save(window, file);
                    Console.WriteLine($"  {Path.GetFileName(file)}");
                    written++;
                }
            }
            finally
            {
                window.Close();
            }
        }
        Console.WriteLine($"{written} Bilder geschrieben.");
        return 0;
    }

    /// <summary>
    /// Hauptfenster außerhalb des Bildschirms in fester Größe, ohne Aktivierung und ohne Mica – auch für die
    /// versteckte Messung (<c>--messen</c>).
    /// </summary>
    internal static MainWindow CreateWindow(AppState state, bool dark)
    {
        ThemeManager.Initialize(forceDark: dark);
        var window = new MainWindow(state, screenshotMode: true)
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000,
            // Zuerst klein anlegen: Die Größengrenze fragt Windows schon beim Erzeugen ab, der Haken dagegen
            // greift erst danach – die endgültige Größe kommt deshalb nach Show()
            Width = 800,
            Height = 600,
        };
        window.SourceInitialized += (_, _) =>
            HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)?.AddHook(AllowLargerThanScreen);
        window.Show();
        window.Width = Width;
        window.Height = Height;
        return window;
    }

    /// <summary>Ohne sichtbaren Desktop ist der „Bildschirm“ oft nur 1024×768 groß – Fenster dürfen hier größer sein.</summary>
    private static nint AllowLargerThanScreen(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != Native.WM_GETMINMAXINFO) return 0;
        var info = Marshal.PtrToStructure<Native.MINMAXINFO>(lParam);
        info.ptMaxSize = info.ptMaxTrackSize = new Native.POINT { X = 16_000, Y = 16_000 };
        Marshal.StructureToPtr(info, lParam, false);
        return 0;
    }

    internal static void Save(Window window, string file)
    {
        window.UpdateLayout();
        var root = (FrameworkElement)window.Content;
        double width = root.ActualWidth, height = root.ActualHeight;
        if (Math.Abs(width - Width) > 1 || Math.Abs(height - Height) > 1)
            throw new InvalidOperationException($"Das Fenster hat {width:0}×{height:0} statt {Width:0}×{Height:0} Punkte – die Bilder wären verzerrt.");
        var bitmap = new RenderTargetBitmap((int)Math.Round(width * Scale), (int)Math.Round(height * Scale),
            96 * Scale, 96 * Scale, PixelFormats.Pbgra32);

        // Den Hintergrund zeichnet das Fenster selbst, nicht sein Inhalt – deshalb zuerst eine Fläche darunter
        var brush = Application.Current.TryFindResource("ApplicationBackgroundBrush") as Brush ?? window.Background ?? Brushes.White;
        var background = new DrawingVisual();
        using (var dc = background.RenderOpen())
            dc.DrawRectangle(brush, null, new Rect(0, 0, width, height));
        bitmap.Render(background);
        bitmap.Render(root);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(file);
        encoder.Save(stream);
    }
}
