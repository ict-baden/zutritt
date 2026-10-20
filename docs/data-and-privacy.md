<sub><a href="../README.md">Zutritt</a> › <a href="../README.md#documentation">Documentation</a> › Data &amp; privacy</sub>

# Data & privacy

What Zutritt reads, what it keeps where, how to delete it, which connections it makes, and why a run is treated as confidential.

**Contents:** [In short](#in-short) · [Read-only](#read-only) · [What is stored](#what-is-stored) · [Where it is stored](#where-it-is-stored) · [Deleting runs](#deleting-runs) · [Exported files](#exported-files) · [Log](#log) · [Diagnostics package and support](#diagnostics-package-and-support) · [Network connections](#network-connections)

## In short

- **Zutritt sends no data to ICT-Baden** – no telemetry, no crash reports, no usage statistics, no update check. The *Info* page and `Zutritt.exe --info` say so: *Diese Software sendet keine Daten an ICT-Baden.*
- **Everything stays on the PC that scans:** the run history in a per-user database, a short log, and the reports you save.
- **The operator is responsible for the data.** A run contains names of people, their group memberships and their access. Whoever runs Zutritt – the customer – is the controller in the sense of the GDPR; ICT-Baden processes none of it through the software.
- **Only one setting is stored: the language**, and only if you choose one on the *Info* page – in `%LocalAppData%\Zutritt\settings.json`. Zutritt also reads `%ProgramData%\Zutritt\settings.json` (a language preset by the administrator) and the environment variable `ZUTRITT_LANGUAGE`. Scan options are chosen per scan and saved with the run; Zutritt keeps no settings in the registry (it only reads whether Windows uses the light or dark theme). The MSI writes one registry value for its Start menu entry ([Installation › MSI](installation.md#msi-installer)).

## Read-only

Zutritt **never changes** permissions, files, shares, accounts or group memberships – not on the PC it runs on and not on the servers it scans. It reads:

| Source | What | What not |
|---|---|---|
| Folders and files | names, owner, permissions (DACL) – opened with `READ_CONTROL` only; files only with `--dateien` | file contents, timestamps, sizes, audit settings (SACL) |
| Shares | name, local path, remark, share permissions | open sessions, open files |
| Account databases and Active Directory | account name, display name, kind, *disabled* flag, group memberships | passwords, e-mail addresses, other attributes |

The only things Zutritt writes are its own database, its [log](#log), the language you choose on the *Info* page (`settings.json`), and the Excel, PDF or diagnostics files you ask for.

## What is stored

A run is a map of who can access what. For each run, the database keeps:

| Part | Content |
|---|---|
| Run | start and end time, PC, who scanned (*DOMÄNE\\name*), Zutritt version, the targets and options, counts |
| Shares | server, name, local path, remark, admin-share flag, whether the permissions were readable, and the share permissions (SID, level, allow/deny) |
| Folders | only the [stored folders](how-it-works.md#what-gets-stored): path, depth, file or folder, share, owner SID, inheritance flags, read error – and every ACE with SID, rights mask, allow/deny, inherited/explicit and inheritance flags |
| Accounts | for every SID in the run: name, display name, kind, local or domain, disabled, whether members were readable – and the direct members of each group |
| Findings | rule, severity, path, account SID and message |
| Errors | path and message of everything that could not be read |

Folders that inherit unchanged are only counted, not stored by name. File names appear only with `--dateien` and only for files with permissions of their own.

**Runs are immutable.** Saving always adds a new run; a stored run is never modified afterwards. A signed report can therefore be recreated from the same run at any time (`Zutritt.exe bericht <id>`).

## Where it is stored

**Per user, in the profile:** `%LocalAppData%\Zutritt\zutritt.db` – a single SQLite file – and the log in `%LocalAppData%\Zutritt\logs`. The *Info* page shows the path and opens the folder.

This is deliberate. A run is a map of the permissions of a file server – including which folders are open to whom. Stored under `%ProgramData%`, it would be readable by every local user through the default permissions there. In the profile, the folder inherits the permissions of your profile: you, *SYSTEM* and the administrators of the PC.

Consequences:

- **Each Windows user has their own history.** A scheduled task running under a service account stores its runs in *that* account's profile. The window always opens your own `%LocalAppData%\Zutritt\zutritt.db`; `--db` is a command-line option only (`scan`, `liste`, `bericht`). To see a task's runs in the window, run the task under your account, or let it write to that file with `--db` (its account then needs write access there). Runs in any other `--db` location can only be listed and reported with `liste` and `bericht` – protect that location accordingly.
- **`--db` on a network share** works: Zutritt then uses the classic rollback journal instead of WAL, which is not reliable over SMB. A database created locally in WAL mode is switched over permanently the first time it is opened through a share – as long as it is not still open on the PC where it lives; otherwise Zutritt refuses with an explanation instead of risking damage.
- **Foreign or newer files are left alone.** Zutritt recognises its own databases by an application ID and refuses to modify any other SQLite file, or a database written by a newer Zutritt version.

The demo (`--demo`) uses a separate temporary database that is deleted when the window closes; your real history is not touched.

## Deleting runs

There is no automatic retention: runs accumulate until you delete them.

- **App:** *Übersicht* › *Letzte Läufe* › select a run › <kbd>Del</kbd> or *Lauf löschen*. The run and everything belonging to it is removed, and the database file shrinks accordingly.
- **Everything:** close Zutritt and delete `%LocalAppData%\Zutritt` (database and log).

Uninstalling the MSI does not remove the database.

## Exported files

Excel and PDF files contain the same map in a form that is easy to pass around: names of people, their group memberships, SIDs, paths and who can access them. The PDF says so on its cover (*Vertraulich – enthält Angaben zu Konten und Zugriffsrechten. Nur für den internen Gebrauch und für Prüfer bestimmt.* – confidential, for internal use and auditors only) and marks every page *Vertraulich* in the footer.

- Store them where only the people who need them – admins, auditors, data owners – can read them.
- They contain personal data (names of employees and their access); handle them according to your data protection rules.
- A fixed report path in a scheduled task overwrites last week's file. Keep older ones on purpose, or recreate them from the stored runs.

## Log

For support, Zutritt keeps a short log: one file per day, `%LocalAppData%\Zutritt\logs\zutritt-YYYY-MM-DD.log`, at most 1 MB per day, deleted after **7 days**.

| In the log | Not in the log |
|---|---|
| start and end with version, build, Windows version, whether the PC is in a domain (yes/no) | targets, paths of scanned folders |
| per scan: the options, duration and counts (folders, shares, accounts, findings per severity) | permissions, owners, group members |
| the most frequent kinds of read errors (with group, domain and path names in them replaced), error messages and exception types, unexpected errors with stack trace | account names, SIDs, report contents |

Error messages from Windows or .NET can contain a file path, for example of a report that could not be written. The log never leaves the PC unless you send it.

## Diagnostics package and support

*Info* › ***Diagnosepaket erstellen*** saves a ZIP file for a support request: version, build, Windows and .NET version, language and time zone, database schema and number of stored runs, and the logs of the last 7 days. It contains **no runs, no reports and no database**.

The switch ***Ohne Namen und Adressen*** is on by default: names of this PC, its user and domain, server names and UNC paths, local paths, account SIDs, *DOMÄNE\\name* accounts, quoted names, IP and e-mail addresses are replaced (*RECHNER*, *BENUTZER*, *SERVER-1* …). Look into the ZIP before you send it.

- **Free support on GitHub is public.** Attach only the anonymised package – never reports, `zutritt.db` or real server, share and account names ([SUPPORT.md](../SUPPORT.md)).
- **Confidential analysis** of real runs or reports is part of a support contract with a data processing agreement (*Auftragsverarbeitungsvertrag*), because ICT-Baden then processes personal data on the customer's behalf.

## Network connections

Zutritt connects only to what it scans and resolves: the file servers (SMB and the Windows network APIs for shares and local accounts), the LSA of these servers and of this PC, and – on domain-joined PCs – the domain controllers of its own domain. There is no telemetry, no update check and no other internet connection.

The *Info* page has links to the source code, help and support, this page, the Impressum and the ICT-Baden website; the licence texts are built into the program and shown in a window. They open in your browser **only when you click them**, and only `http`/`https` addresses are opened.

---

<p align="center"><sub>
<a href="installation.md">Installation</a> · <a href="usage.md">Using the app</a> · <a href="command-line.md">Command line</a> · <a href="rules.md">Rules</a> · <a href="how-it-works.md">How it works</a> · <a href="excel-and-pdf.md">Excel &amp; PDF</a> · <b>Data &amp; privacy</b> · <a href="limitations.md">Limitations</a> · <a href="development.md">Development</a><br>
<a href="../README.md">← Back to Zutritt</a>
</sub></p>
