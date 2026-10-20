// SPDX-License-Identifier: MIT
using System.Diagnostics;
using System.Security.Principal;
using Zutritt.Tests.Analysis;
using Zutritt.Tests.Identity;
using Zutritt.Tests.Scanning;
using Zutritt.Tests.Storage;
using Zutritt.Tests.TestSupport;
using Zutritt.Tests.Windows.Analysis;
using Zutritt.Tests.Windows.Scanning;

namespace Zutritt.Tests.Windows.Identity;

/// <summary>
/// Legt lokale Testkonten und -gruppen an (Präfix „zt-test-“) und räumt sie beim Dispose wieder ab.
/// Braucht Administratorrechte; nur zusammen mit <c>[AdminFact]</c> verwenden.
/// </summary>
internal sealed class LocalAccounts : IDisposable
{
    private readonly List<string> _users = [];
    private readonly List<string> _groups = [];

    public static string NewName() => "zt-test-" + Guid.NewGuid().ToString("N")[..8];

    public string CreateUser(string? fullName = null, bool disabled = false)
    {
        var name = NewName();
        // 13 Zeichen: erfüllt die Komplexitätsregeln und bleibt unter 15 (sonst fragt „net user“ nach).
        var password = "Zt!" + Guid.NewGuid().ToString("N")[..8] + "a1";
        var args = new List<string> { "user", name, password, "/add", "/y" };
        if (fullName is not null) args.Add($"/fullname:{fullName}");
        if (disabled) args.Add("/active:no");
        // Vorher vormerken: Auch wenn „net“ einen Fehler meldet, kann das Konto schon angelegt sein – dann muss es weg.
        _users.Add(name);
        Net(args);
        return name;
    }

    public string CreateGroup()
    {
        var name = NewName();
        _groups.Add(name);
        Net(["localgroup", name, "/add"]);
        return name;
    }

    public static void AddMember(string group, string member) => Net(["localgroup", group, member, "/add"]);

    public void DeleteUser(string name)
    {
        Net(["user", name, "/delete"]);
        _users.Remove(name);
    }

    public static string SidOf(string name) =>
        new NTAccount(Environment.MachineName, name).Translate(typeof(SecurityIdentifier)).Value;

    public void Dispose()
    {
        foreach (var user in _users) TryNet(["user", user, "/delete"]);
        foreach (var group in _groups) TryNet(["localgroup", group, "/delete"]);
        _users.Clear();
        _groups.Clear();
    }

    private static void Net(IEnumerable<string> args)
    {
        var (code, output) = Run(args);
        // Nur die ersten beiden Argumente nennen – das dritte kann ein Kennwort sein.
        if (code != 0) throw new InvalidOperationException($"net {string.Join(' ', args.Take(2))} … fehlgeschlagen ({code}): {output}");
    }

    private static void TryNet(IEnumerable<string> args)
    {
        try { Run(args); }
        catch (Exception) { /* Aufräumen ist best effort */ }
    }

    private static (int Code, string Output) Run(IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo("net.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.StandardInput.Close();
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(60_000))
        {
            p.Kill();
            return (-1, "Zeitüberschreitung");
        }
        return (p.ExitCode, stdout.Result + stderr.Result);
    }
}
