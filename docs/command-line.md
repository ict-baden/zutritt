<sub><a href="../README.md">Zutritt</a> › <a href="../README.md#documentation">Documentation</a> › Command line</sub>

# Command line

The same `Zutritt.exe` that opens the window also runs without one: given a command, it scans, stores the run, writes reports and ends with an exit code – suitable for scheduled tasks and monitoring. Output and reports follow the language of the window ([Using the app › Languages](usage.md#languages)); for scripts, set it with the environment variable `ZUTRITT_LANGUAGE` (`en`, `de`, `es`, `fr`, `zh-Hans`). Every command and option has an English spelling as well (`list`, `report`, `--files`, `--depth`, `--deep-from`, `--no-groups`, `--include-admin-shares`, `--report`, `--compare`, `--help`, `--licenses`); the German ones below keep working. Errors end with a code such as `ZUT-0202` ([Error codes](error-codes.md)).

**Contents:** [Overview](#overview) · [Targets](#targets) · [scan](#scan) · [liste](#liste) · [bericht](#bericht) · [Exit codes](#exit-codes) · [Waiting for the end](#waiting-for-the-end) · [Scheduled tasks](#scheduled-tasks) · [Logs and encoding](#logs-and-encoding) · [Other switches](#other-switches)

## Overview

```text
Zutritt.exe                         start the window
Zutritt.exe scan <ziel> [<ziel> …] [Optionen]
Zutritt.exe liste [--db <pfad>]
Zutritt.exe bericht <id> --bericht <datei> [--vergleich] [--db <pfad>]
Zutritt.exe --demo                  window with built-in demo data
Zutritt.exe --screenshots <ordner> [--theme hell|dunkel|beide]
Zutritt.exe --hilfe                 help (also --help, -h, /?)
Zutritt.exe --version               version and build (also -v)
Zutritt.exe --info                  version, licence, links, database and log location
Zutritt.exe --lizenzen              licence text and third-party licences
```

| Command | Does |
|---|---|
| `scan` | Scans the targets, saves the run, optionally compares with the previous run and writes reports |
| `liste` | Lists the saved runs with their ID |
| `bericht` | Writes reports for a saved run afterwards |
| `--version`, `--info`, `--lizenzen` | Information about Zutritt itself – see [Other switches](#other-switches) |

Commands and options are not case-sensitive. Options start with `--`; everything else is a target (for `scan`) or the run ID (for `bericht`). An unknown command or option ends with an error message and exit code 3.

## Targets

| Target | Scans |
|---|---|
| `\\server\freigabe[\ordner]` | a share, or a folder in it |
| `\\server` | all disk shares of a server |
| `D:\Daten` | a local folder; `D:` alone means the whole drive |
| `.` | all disk shares of this PC, through their local paths |

Several targets can be given in one run. A target that lies inside another target is scanned only once, as its own root. Device paths (`\\.\…`, `\\?\Volume{…}`) are rejected.

## scan

```bat
Zutritt.exe scan \\FS01 --vergleich --bericht D:\Berichte\Zutritt.pdf --bericht D:\Berichte\Zutritt.xlsx
```

<table>
  <thead>
    <tr><th width="200">Option</th><th>Meaning</th><th>Default</th></tr>
  </thead>
  <tbody>
    <tr><td><code>--dateien</code></td><td>Also record <b>files</b> with their own permissions (takes longer)</td><td>folders only</td></tr>
    <tr><td><code>--tiefe N</code></td><td>At most <code>N</code> levels below each target; <code>0</code> = only the targets themselves</td><td>unlimited</td></tr>
    <tr><td><code>--ausnahme-ab N</code></td><td>Report broken inheritance from level <code>N</code> on as <i>tief versteckte Ausnahme</i> (<a href="rules.md#zt06--deeply-hidden-exception">ZT06</a>); <code>N</code> ≥ 1</td><td><code>3</code></td></tr>
    <tr><td><code>--ohne-gruppen</code></td><td>Do not resolve groups down to people</td><td>groups are resolved</td></tr>
    <tr><td><code>--mit-admin-freigaben</code></td><td>Also scan <code>C$</code>, <code>ADMIN$</code> and other shares ending in <code>$</code></td><td>skipped</td></tr>
    <tr><td><code>--bericht &lt;datei&gt;</code></td><td>Write a report: <code>.xlsx</code> (Excel) or <code>.pdf</code>. Can be given several times.</td><td>none</td></tr>
    <tr><td><code>--vergleich</code></td><td>Compare with the last run <b>with the same targets</b> and include the changes in the reports</td><td>no comparison</td></tr>
    <tr><td><code>--db &lt;pfad&gt;</code></td><td>Use a different database</td><td><code>%LocalAppData%\Zutritt\zutritt.db</code> (per user)</td></tr>
  </tbody>
</table>

Notes:

- **Report paths** are checked before the scan starts – only `.xlsx` and `.pdf` are accepted. Missing folders are created. A report is first written to a temporary file next to the target and then moved into place, so an existing report is never left half-written; if the file is open in Excel or a PDF viewer, writing fails with a clear message. One failed report does not stop the others, but the exit code becomes 3.
- **Environment variables** in `--bericht` and `--db` are expanded by Zutritt itself, so `--bericht %USERPROFILE%\Berichte\Zutritt.pdf` also works in PowerShell.
- **The comparison** looks for the newest earlier run whose set of targets is the same (order, upper/lower case, a trailing `\` and `D:` instead of `D:\` do not matter), preferring one with the same options; with other options only the common scope is compared and the summary says *Achtung: Umfang weicht ab …* ([How it works › Comparison](how-it-works.md#comparison)). For targets that depend on the machine – `.`, local paths, `\\localhost\…` – only runs from the same PC count. Without such a run, Zutritt says so (*Vergleich: Es gibt noch keinen früheren Lauf mit denselben Zielen.*) and continues.
- <kbd>Ctrl</kbd>+<kbd>C</kbd> while targets, folders and accounts are processed cancels cleanly: *Abgebrochen.*, exit code 3, nothing is saved. Once the findings are being evaluated (*Befunde*), the run is completed and saved.

**Output** (example, shortened). In a console window the progress is one line that updates itself; when the output is redirected, only the phases are printed.

```text
Zutritt 0.1.0 – Scan von \\FS01
Ziele …
Ordner …
Konten …
Befunde …
Fertig …
Lauf 12 gespeichert in C:\Users\admin\AppData\Local\Zutritt\zutritt.db
Bericht geschrieben: D:\Berichte\Zutritt.pdf

  Ziele            \\FS01
  Dauer            4:37 min
  Ordner geprüft   18.734, davon 41 mit eigenen Rechten
  Nicht lesbar     2
  Befunde          Hoch 5 · Mittel 15 · Niedrig 7 · Info 9

Wichtigste Befunde:
  Hoch    ZT01  \\FS01\Labor\Austausch
  Hoch    ZT05  \\FS01\Labor\QC
  …
  … und 5 weitere (alle im Bericht)

Seit dem Lauf vom 05.10.2026, 06:00: 4 neue, 3 behobene Befunde, 79 Zugriffsänderungen, …

Exitcode 2: mindestens ein Befund „Hoch“
```

The summary lists at most 15 findings of severity *Mittel* or *Hoch*; all findings are in the database and the reports. Numbers and dates use the regional format of Windows (German: `18.734` = 18,734).

## liste

```bat
Zutritt.exe liste
Zutritt.exe liste --db D:\Zutritt\zutritt.db
```

```text
Gespeicherte Läufe in C:\Users\admin\AppData\Local\Zutritt\zutritt.db:

    Id  Datum                Ordner  Befunde   Hoch  Ziele
    12  12.10.2026, 06:00    18.734       36      5  \\FS01
    11  05.10.2026, 06:00    18.702       35      4  \\FS01
```

*Ordner* is the number of folders checked, *Hoch* the number of high findings. Without a database, `liste` says so and ends with exit code 0.

## bericht

```bat
Zutritt.exe bericht 12 --bericht D:\Berichte\FS01-Oktober.pdf --vergleich
```

Writes one or more reports for a saved run, as stored – the findings are not re-evaluated. `--vergleich` compares with the run before it that has the same targets. Exit code 0 on success, 3 if the run does not exist or a report could not be written.

## Exit codes

| Code | `scan` | `liste`, `bericht` |
|---|---|---|
| `0` | no findings of severity *Mittel* or higher | success |
| `1` | findings *Mittel*, none *Hoch* | – |
| `2` | at least one finding *Hoch* | – |
| `3` | error or cancellation, the run could not be saved, a report could not be written, or **no target could be checked at all** | error |

> [!WARNING]
> **A partly unreachable scan is not an error in this sense.** If some servers, shares or folders cannot be read, the scan completes: the problem becomes a finding [ZT09](rules.md#zt09--folder-not-readable) or [ZT13](rules.md#zt13--share-permissions-not-readable) of severity *Info*, which does not raise the exit code, and the summary says *Achtung: N von M Zielen bzw. Freigaben konnten nicht geprüft werden*. M counts each target, and for a whole server (`\\server`) or `.` each of its shares; a server that does not answer or whose share list cannot be read counts as one target not checked. A folder whose own permissions cannot be read still counts as checked if Zutritt could read something below it. Only when **nothing** could be checked at all – the server is off, the only share is not readable – the exit code is `3`, so that a scheduled task does not look clean. When you monitor scheduled runs, also check the *Info* count and *Ordner geprüft* in the summary, or the *Fehler* sheet of the Excel report.

An MSI update or uninstall closes a running Zutritt; a scan stopped this way ends with exit code `3`, like one cancelled with Ctrl+C.

If the run cannot be saved (database read-only, locked, disk full), the reports given with `--bericht` are written anyway and the exit code is `3`. The database and the report folders are checked **before** the scan: if they cannot be written, Zutritt stops at once with exit code `3` and scans nothing.

## Waiting for the end

`Zutritt.exe` is a Windows (GUI) program. When started from a command prompt, it attaches to that console for its output, but the prompt does not wait for it. To wait – and to get `%ERRORLEVEL%` or `$LASTEXITCODE` right:

| Shell | Command |
|---|---|
| cmd | `start /wait Zutritt.exe scan …` |
| PowerShell | `Zutritt.exe scan … \| Out-Default` |

Inside a `.cmd` file, `cmd` waits on its own. Task Scheduler always waits and records the exit code as the task's *Last Run Result*.

## Scheduled tasks

Every Monday at 6:00, with comparison and a PDF to sign (the example from `--hilfe`):

```bat
schtasks /create /tn "Zutritt Wochenbericht" /sc weekly /d MO /st 06:00 /tr ^
  "\"C:\Program Files\Zutritt\Zutritt.exe\" scan \\FS01 --vergleich --bericht D:\Berichte\Zutritt.pdf"
```

Things to decide when you create the task:

- **The account.** The task needs the [rights for scanning](installation.md#rights-for-scanning) – typically an account that is local administrator on the file servers. Add `/ru <account> /rp *` to `schtasks`, or set it in the Task Scheduler UI, and use *Run with highest privileges* (`/rl highest`) when local folders are scanned.
- **The database.** Without `--db`, runs go to `%LocalAppData%\Zutritt\zutritt.db` **of the task's account** – if that is not your own account, they do not show up in your window, and `--vergleich` compares only with that account's history. The window has no `--db` option: it always opens the database of the user who starts it. To see the task's runs there, run the task under your own account, or point the task's `--db` at the database in your profile, e.g. `--db C:\Users\<you>\AppData\Local\Zutritt\zutritt.db` (the task's account then needs write access to that folder). Runs in any other database can be listed and reported with `liste --db …` and `bericht <id> --db …`. See [Data & privacy](data-and-privacy.md) before choosing a shared location.
- **The report file.** A fixed name such as `Zutritt.pdf` is overwritten every week. Run a small `.cmd` file that builds a dated name if you want to keep every report – the database keeps every run anyway, and `bericht <id>` can recreate any report later.

## Logs and encoding

Independently of any redirection, Zutritt keeps a short log of its own for support – start, options, counts and errors, no permissions or account names – in `%LocalAppData%\Zutritt\logs` for 7 days ([Data & privacy › Log](data-and-privacy.md#log)).

To keep the output of a scheduled run, redirect inside a `.cmd` file:

```bat
@echo off
rem zutritt-woche.cmd
"C:\Program Files\Zutritt\Zutritt.exe" scan \\FS01 --vergleich ^
  --bericht D:\Berichte\Zutritt.pdf > D:\Berichte\zutritt.log 2>&1
exit /b %ERRORLEVEL%
```

Zutritt chooses the encoding per destination, so umlauts and typographic quotes arrive intact:

| Output goes to | Encoding |
|---|---|
| a console window | Unicode, without changing the console's code page |
| a file (`> log.txt`, `>> log.txt`) | UTF-8; a byte order mark only if the file is still empty, so appending and `2>&1` stay clean |
| a pipe (`\| more`, `$x = & Zutritt.exe …`) | the code page of the calling console – UTF-8 if it is set to 65001, otherwise that code page (usually OEM 437 or 850), with typographic characters replaced by similar ASCII ones (`„“` → `"`, `–` → `-`) |

Known pitfalls:

- **PowerShell 7.4 and later** pass the bytes of `& Zutritt.exe … > log.txt` through unchanged. With an OEM console the file then contains OEM text. Use the `.cmd` redirection above, or set `[Console]::OutputEncoding = [Text.Encoding]::UTF8` first.
- **`> NUL` does not silence Zutritt.** The NUL device looks like a console, so the output still appears in the console window.

## Other switches

| Switch | Does |
|---|---|
| `--demo` | Opens the window with two built-in demo runs (domain *MUSTER*, server *FS01*) in a temporary database that is deleted on exit. Must be the only argument. |
| `--screenshots <ordner>` | Renders every page of the demo as PNG into the folder – light, dark or both (default), chosen with `--theme hell`, `dunkel` or `beide`. Used for this documentation; the window stays off-screen, but the Windows session must be connected – in a disconnected session it stops with exit code 3 instead of writing empty images. |
| `--version`, `-v` | Prints name, version and build, e.g. `Zutritt 0.1.0-ci.12 (Build abc1234)`. Use it when you report a problem. |
| `--info` | Prints what the *Info* page shows: version and build, .NET version, © and licence, where the licence texts are, links to source code, help and support, privacy, Impressum and website, the database and log location, and *Diese Software sendet keine Daten an ICT-Baden.* |
| `--lizenzen` | Prints the MIT licence of Zutritt followed by `THIRD-PARTY-NOTICES.txt` – the licences of all libraries it contains. Also `--licenses`. |

`--version`, `--info`, `--lizenzen` and `--demo` stand alone: extra arguments end with an error and exit code 3. Otherwise they end with exit code 0.

---

<p align="center"><sub>
<a href="installation.md">Installation</a> · <a href="usage.md">Using the app</a> · <b>Command line</b> · <a href="rules.md">Rules</a> · <a href="how-it-works.md">How it works</a> · <a href="excel-and-pdf.md">Excel &amp; PDF</a> · <a href="data-and-privacy.md">Data &amp; privacy</a> · <a href="limitations.md">Limitations</a> · <a href="development.md">Development</a><br>
<a href="../README.md">← Back to Zutritt</a>
</sub></p>
