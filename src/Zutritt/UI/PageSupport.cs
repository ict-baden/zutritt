// SPDX-License-Identifier: MIT
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Zutritt.Core;
using Zutritt.Core.Model;
using Zutritt.Core.Resources;

namespace Zutritt.UI;

/// <summary>Eine Seite rechts im Hauptfenster.</summary>
internal interface IPage
{
    /// <summary>Seite wird sichtbar oder der geladene Lauf hat sich geändert: Inhalt mit <see cref="AppState"/> abgleichen.</summary>
    void OnShown();

    /// <summary>Definierten Zustand für <c>--screenshots</c> herstellen und warten, bis alles geladen ist.</summary>
    Task PrepareScreenshotAsync();
}

/// <summary>Seite mit Suchfeld (Strg+F).</summary>
internal interface ISearchPage
{
    void FocusSearch();
}

internal static class Ui
{
    /// <summary>
    /// Startet Arbeit aus einem Ereignis heraus. Fehler werden als Meldung gezeigt statt abzustürzen – außer im
    /// Screenshot-Modus, dort sollen sie den Lauf sichtbar abbrechen.
    /// </summary>
    public static async void Run(AppState state, Func<Task> work, string failure)
    {
        try
        {
            await work();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (!state.Strict)
        {
            state.Report(NoticeKind.Error, Texts.Format(Strings.Ui_FailureWithReason, failure, ErrorCodes.Describe(ex)));
        }
    }

    /// <summary>Wartet, bis WPF Layout und Rendern erledigt hat (für Screenshots und Scrollen nach dem Laden).</summary>
    public static async Task IdleAsync()
    {
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    public static T? FindChild<T>(DependencyObject root) where T : DependencyObject
    {
        // Breitensuche ohne Rekursion
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            int n = VisualTreeHelper.GetChildrenCount(current);
            for (int i = 0; i < n; i++)
            {
                var child = VisualTreeHelper.GetChild(current, i);
                if (child is T t) return t;
                queue.Enqueue(child);
            }
        }
        return null;
    }

    /// <summary>Anfangsbuchstaben für den Kreis vor Namen („QC Mitarbeitende 01“ → „QM“).</summary>
    public static string Initials(string displayName, string fallback)
    {
        var text = string.IsNullOrWhiteSpace(displayName) ? fallback : displayName;
        text = AccountName.LastPart(text);
        var parts = text.Split([' ', '.', '-', '_'], StringSplitOptions.RemoveEmptyEntries).Where(p => char.IsLetter(p[0])).ToList();
        return parts.Count switch
        {
            0 => "?",
            1 => parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant(),
            _ => $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}",
        };
    }
}

/// <summary>Führt eine Aktion erst aus, wenn eine Weile keine neue Eingabe kam (Suchfelder).</summary>
internal sealed class Debouncer
{
    private readonly DispatcherTimer _timer;
    private Action? _action;

    public Debouncer(TimeSpan delay)
    {
        _timer = new DispatcherTimer { Interval = delay };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            _action?.Invoke();
        };
    }

    public void Run(Action action)
    {
        _action = action;
        _timer.Stop();
        _timer.Start();
    }
}

/// <summary>
/// Mausrad an die Seite weitergeben, wenn eine eingebettete Liste schon am Anfang oder Ende ist – sonst
/// „schluckt“ die Liste das Rad und die Seite lässt sich über ihr nicht mehr scrollen.
/// </summary>
public static class Wheel
{
    public static readonly DependencyProperty BubbleProperty =
        DependencyProperty.RegisterAttached("Bubble", typeof(bool), typeof(Wheel), new PropertyMetadata(false, OnBubbleChanged));

    public static bool GetBubble(DependencyObject o) => (bool)o.GetValue(BubbleProperty);
    public static void SetBubble(DependencyObject o, bool value) => o.SetValue(BubbleProperty, value);

    private static void OnBubbleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        if (e.NewValue is true) element.PreviewMouseWheel += OnPreviewMouseWheel;
        else element.PreviewMouseWheel -= OnPreviewMouseWheel;
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not DependencyObject d || e.Handled) return;
        var inner = Ui.FindChild<ScrollViewer>(d);
        bool atTop = inner is null || inner.VerticalOffset <= 0;
        bool atBottom = inner is null || inner.VerticalOffset >= inner.ScrollableHeight;
        if ((e.Delta > 0 && !atTop) || (e.Delta < 0 && !atBottom)) return;

        e.Handled = true;
        if (VisualTreeHelper.GetParent(d) is UIElement parent)
            parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = sender });
    }
}
