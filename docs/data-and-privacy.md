<sub><a href="../README.md">Zutritt</a> › <a href="../README.md#documentation">Documentation</a> › Data &amp; privacy</sub>

# Data & privacy

What Zutritt reads, what it keeps where, and why a run is treated as confidential.

**Contents:** [Read-only](#read-only) · [What is stored](#what-is-stored) · [Where it is stored](#where-it-is-stored) · [Deleting runs](#deleting-runs) · [Exported files](#exported-files) · [Network connections](#network-connections)

## Read-only

Zutritt **never changes** permissions, files, shares, accounts or group memberships – not on the PC it runs on and not on the servers it scans. It reads:

| Source | What | What not |
|---|---|---|
| Folders and files | names, owner, permissions (DACL) – opened with `READ_CONTROL` only; files only with `--dateien` | file contents, timestamps, sizes, audit settings (SACL) |
| Shares | name, local path, remark, share permissions | open sessions, open files |
| Account databases and Active Directory | account name, display name, kind, *disabled* flag, group memberships | passwords, e-mail addresses, other attributes |

The only things Zutritt writes are its own database and the Excel or PDF files you ask for.

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

**Per user, in the profile:** `%LocalAppData%\Zutritt\zutritt.db` – a single SQLite file. The *Info* page shows the path and opens the folder.

This is deliberate. A run is a map of the permissions of a file server – including which folders are open to whom. Stored under `%ProgramData%`, it would be readable by every local user through the default permissions there. In the profile, the folder inherits the permissions of your profile: you, *SYSTEM* and the administrators of the PC.

Consequences:

- **Each Windows user has their own history.** A scheduled task running under a service account stores its runs in *that* account's profile. The window always opens your own `%LocalAppData%\Zutritt\zutritt.db`; `--db` is a command-line option only (`scan`, `liste`, `bericht`). To see a task's runs in the window, run the task under your account, or let it write to that file with `--db` (its account then needs write access there). Runs in any other `--db` location can only be listed and reported with `liste` and `bericht` – protect that location accordingly.
- **`--db` on a network share** works: Zutritt then uses the classic rollback journal instead of WAL, which is not reliable over SMB. A database created locally in WAL mode is switched over permanently the first time it is opened through a share – as long as it is not still open on the PC where it lives; otherwise Zutritt refuses with an explanation instead of risking damage.
- **Foreign or newer files are left alone.** Zutritt recognises its own databases by an application ID and refuses to modify any other SQLite file, or a database written by a newer Zutritt version.

The demo (`--demo`) uses a separate temporary database that is deleted when the window closes; your real history is not touched.

## Deleting runs

There is no automatic retention: runs accumulate until you delete them.

- **App:** *Übersicht* › *Letzte Läufe* › select a run › <kbd>Del</kbd> or *Lauf löschen*. The run and everything belonging to it is removed, and the database file shrinks accordingly.
- **Everything:** close Zutritt and delete `%LocalAppData%\Zutritt`.

Uninstalling the MSI does not remove the database.

## Exported files

Excel and PDF files contain the same map in a form that is easy to pass around: names of people, their group memberships, SIDs, paths and who can access them. The PDF says so on its cover (*Vertraulich – enthält Angaben zu Konten und Zugriffsrechten. Nur für den internen Gebrauch und für Prüfer bestimmt.* – confidential, for internal use and auditors only) and marks every page *Vertraulich* in the footer.

- Store them where only the people who need them – admins, auditors, data owners – can read them.
- They contain personal data (names of employees and their access); handle them according to your data protection rules.
- A fixed report path in a scheduled task overwrites last week's file. Keep older ones on purpose, or recreate them from the stored runs.

## Network connections

Zutritt connects only to what it scans and resolves: the file servers (SMB and the Windows network APIs for shares and local accounts), the LSA of these servers and of this PC, and – on domain-joined PCs – the domain controllers of its own domain. There is no telemetry, no update check and no other internet connection. The link on the *Info* page opens GitHub in your browser only when you click it.

---

<p align="center"><sub>
<a href="installation.md">Installation</a> · <a href="usage.md">Using the app</a> · <a href="command-line.md">Command line</a> · <a href="rules.md">Rules</a> · <a href="how-it-works.md">How it works</a> · <a href="excel-and-pdf.md">Excel &amp; PDF</a> · <b>Data &amp; privacy</b> · <a href="limitations.md">Limitations</a> · <a href="development.md">Development</a><br>
<a href="../README.md">← Back to Zutritt</a>
</sub></p>
