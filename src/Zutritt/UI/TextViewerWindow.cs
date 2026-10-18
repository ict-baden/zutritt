using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Zutritt.UI.Theme;

namespace Zutritt.UI;

/// <summary>
/// Fenster für lange, nur lesbare Texte (Lizenztext, Drittanbieter-Lizenzen): Festbreitenschrift, scrollbar,
/// markier- und kopierbar, Esc schließt.
/// </summary>
public sealed class TextViewerWindow : Window, IThemedWindow
{
    private TextViewerWindow(string title, string text)
    {
        Title = $"{title} – Zutritt";
        Width = 860;
        Height = 680;
        MinWidth = 480;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        UseLayoutRounding = true;
        SetResourceReference(FontFamilyProperty, "UiFont");
        SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
        SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");

        var box = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            TextWrapping = TextWrapping.NoWrap,
            AcceptsReturn = true,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontSize = 12.5,
            Padding = new Thickness(12, 10, 12, 10),
        };
        box.SetResourceReference(FontFamilyProperty, "MonoFont");
        System.Windows.Automation.AutomationProperties.SetName(box, title);

        var close = new Button { Content = "Schließen", IsCancel = true, IsDefault = true, MinWidth = 110, HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();

        var root = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(close, Dock.Bottom);
        close.Margin = new Thickness(0, 12, 0, 0);
        root.Children.Add(close);
        root.Children.Add(box);
        Content = root;

        Loaded += (_, _) =>
        {
            box.Focus();
            box.CaretIndex = 0;
            box.ScrollToHome();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
        SourceInitialized += (_, _) => ThemeChanged();
    }

    /// <summary>Text in einem eigenen Fenster über <paramref name="owner"/> zeigen.</summary>
    public static void Show(Window? owner, string title, string text)
    {
        var window = new TextViewerWindow(title, text) { Owner = owner };
        if (owner is null) window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.ShowDialog();
    }

    public void ThemeChanged()
    {
        ThemeManager.ApplyWindowChrome(this, mica: false);
        SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
    }
}
