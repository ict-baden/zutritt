// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Zutritt.Tests;

/// <summary>
/// Die Tests prüfen die deutschen Texte (Strings.de.resx) und deutsche Zahlen- und Datumsformate – unabhängig davon,
/// in welcher Sprache der Rechner läuft. Tests für andere Sprachen setzen die Kultur selbst.
/// </summary>
internal static class TestCulture
{
    public static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    [ModuleInitializer]
    internal static void UseGerman()
    {
        CultureInfo.DefaultThreadCurrentCulture = German;
        CultureInfo.DefaultThreadCurrentUICulture = German;
        CultureInfo.CurrentCulture = German;
        CultureInfo.CurrentUICulture = German;
        // Zutritt als eigener Prozess (Befehlszeile) erbt die Variable und spricht dann ebenfalls Deutsch
        Environment.SetEnvironmentVariable(Zutritt.Core.Localization.UiLanguage.EnvironmentVariable, "de");
    }

    /// <summary>Für die Dauer eines Tests eine andere Sprache (Oberfläche und Formate).</summary>
    public static IDisposable Use(string name)
    {
        var (culture, ui) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
        return new Restore(() => (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (culture, ui));
    }

    private sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }
}
