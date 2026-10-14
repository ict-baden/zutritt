<sub><a href="../README.md">Zutritt</a> › <a href="../README.md#documentation">Documentation</a> › Installation &amp; rights</sub>

# Installation & rights

Which file to download, what it needs, and which rights the scanning account needs.

**Contents:** [Requirements](#requirements) · [Downloads](#downloads) · [MSI installer](#msi-installer) · [Single exe and ZIP](#single-exe-and-zip) · [Rights for scanning](#rights-for-scanning) · [Recommended setup](#recommended-setup) · [Updates and uninstalling](#updates-and-uninstalling)

> [!IMPORTANT]
> **Pre-release.** Zutritt is built and tested in GitHub Actions on every push to `main` (and for pull requests and `v*` tags), but it is not yet released as a stable version. Program and MSI are **not code-signed**; Windows SmartScreen may warn when you start them.

## Requirements

| Topic | Requirement |
|---|---|
| **Operating system** | 64-bit Windows 10 version 1809 (build 17763) or later, or Windows 11. Builds are produced for `win-x64` only. |
| **.NET** | None for the MSI and for `Zutritt.exe` – both carry the .NET 10 runtime. The ZIP variant needs the **.NET 10 Desktop Runtime**. |
| **Network** | The scanning PC must reach the file servers over SMB. Shares, share permissions, local accounts and account names are queried with the standard Windows network APIs (`NetShareEnum`, `NetShareGetInfo`, `NetLocalGroupGetMembers`, LSA lookups). |
| **Active Directory** | Optional. Domain accounts are resolved and domain groups expanded only when the scanning PC is a **member of the domain** and a domain controller is reachable. |
| **Language** | The user interface, the command-line output and the reports are German, regardless of the Windows display language. |

## Downloads

All files are attached to the pre-release [**latest**](https://github.com/ict-baden/zutritt/releases/tag/latest), which GitHub Actions rebuilds on every push to `main`. Tagged versions (`v1.2.3`) get their own release with the same files.

| File | Runtime | Use it for |
|---|---|---|
| `Zutritt-<version>-x64.msi` | included | Installing for all users of a PC, also through software distribution. |
| `Zutritt.exe` | included | Running without installation, e.g. from an admin share or a USB stick. |
| `Zutritt-<version>-win-x64-framework.zip` | .NET 10 Desktop Runtime | A small download where the runtime is already installed. |

Builds from `main` carry versions like `1.0.0-ci.12` (12 = the number of the build run); releases from a tag carry the tag's version.

## MSI installer

The MSI installs per machine:

- program files into `C:\Program Files\Zutritt`,
- one Start menu entry *Zutritt*,
- **no** service, **no** autostart, no scheduled task, no changes to other settings.

```powershell
# As administrator. The file name contains the version of the build.
msiexec /i Zutritt-1.0.0-ci.12-x64.msi            # interactive
msiexec /i Zutritt-1.0.0-ci.12-x64.msi /qn        # silent, e.g. for software distribution
msiexec /x Zutritt-1.0.0-ci.12-x64.msi /qn        # uninstall
```

The installer closes a running Zutritt before it replaces the files.

## Single exe and ZIP

**`Zutritt.exe`** is the whole program in one file – runtime and native libraries (SQLite, WPF) are embedded. Copy it anywhere and start it. Because it is not signed, SmartScreen may show *Windows protected your PC*; *More info* › *Run anyway* starts it.

**The ZIP** contains a smaller, framework-dependent build and a copy of the project's `README.md` (its images and links point into the repository and only work on GitHub). Extract it and start `Zutritt.exe`; Windows asks for the .NET 10 Desktop Runtime if it is missing.

All variants behave the same: same features, same command line, same database location (`%LocalAppData%\Zutritt\zutritt.db` of the user who runs it – see [Data & privacy](data-and-privacy.md)).

## Rights for scanning

Zutritt only reads, but it can only report what the scanning account is allowed to read. It runs with the rights of the user who starts it and **does not request elevation** itself (manifest level `asInvoker`). To use your administrator rights on the local PC, start it with *Run as administrator*.

| What Zutritt reads | How | Rights needed | If they are missing |
|---|---|---|---|
| Owner and permissions (DACL) of each folder | Opened with `READ_CONTROL` only | *Read permissions* on the folder | Finding **ZT09** *Ordner nicht lesbar* for that folder; its subfolders are still scanned if the folder can be listed (each is then stored with its own permissions) |
| Contents of each folder | Directory listing | *List folder / read data* | Finding **ZT09** with “Inhalt nicht auflistbar”; the folder's own permissions are still shown |
| Folders over the network | SMB | The share permission must let the account in | The path is unreachable – ZT09 |
| List of shares for `\\server` and `.` | `NetShareEnum` level 1 | Usually any authenticated user | Error, nothing scanned for that server |
| Share permissions and local path of a share | `NetShareGetInfo` level 502 | **Administrator on the server** | Finding **ZT13**; network access is rated by NTFS alone (an upper bound). For `.` the local path is unknown, so that share is not scanned. |
| Local users and groups of the server (also *VORDEFINIERT* / BUILTIN groups) | SAM queries on the server | Current Windows versions allow remote SAM queries only for administrators by default | Note in the run; members of local and BUILTIN groups are missing and the *disabled* state of local accounts is unknown. Names still come from the server's LSA, so local accounts stay *Unbekannt* (unknown) only if that lookup fails too |
| Account names | LSA lookups on the scanned server and on this PC | Usually any authenticated user | Accounts stay *Unbekannt* |
| Domain users and groups | Active Directory of the PC's own domain | A normal domain account can usually read users and groups | Note “Active Directory … nicht erreichbar”; domain accounts stay *Unbekannt*, their groups are not expanded |

**Backup privilege.** Zutritt opens every object with backup intent (`FILE_OPEN_FOR_BACKUP_INTENT`). If the backup privilege (*SeBackupPrivilege*) is already **enabled** in the process token, Windows then lets Zutritt read the permissions regardless of the ACL. Zutritt does not enable the privilege itself; whether it is enabled depends on how the process was started.

> [!TIP]
> Unreadable areas are never silently skipped: they appear as ZT09 or ZT13 findings, in the tile *nicht lesbar* on the overview, on the *Fehler* sheet of the Excel report and under *Nicht geprüfte Bereiche* in the PDF.

## Recommended setup

- **Scan from an admin workstation** with an account that is a local administrator on the file servers you scan. This gives Zutritt the share permissions (level 502) and the servers' local groups.
- **For local folders** start Zutritt *as administrator*; otherwise folders that only administrators may read show up as unreadable.
- **On a domain-joined PC**, so that domain groups are expanded down to people. In a workgroup only local accounts are resolved.
- **For scheduled runs**, create the task under that same account and enable *Run with highest privileges* if local folders are included. The task's account also determines where the run history is stored – see [Command line › Scheduled tasks](command-line.md#scheduled-tasks).

## Updates and uninstalling

- **MSI:** install the newer MSI over the old one. Builds from `main` replace each other (Windows Installer compares only the first three parts of the version, and the package allows same-version upgrades); a release with a higher version replaces them as well. Installing an *older* version over a newer one is refused (*Eine neuere Version von Zutritt ist bereits installiert.*).
- **Exe and ZIP:** replace the files.
- **Database:** a newer Zutritt upgrades the database schema automatically. An older Zutritt refuses to open a database written by a newer version instead of damaging it.
- **Uninstalling** removes the program files and the Start menu entry. The run history in `%LocalAppData%\Zutritt` of each user stays; delete that folder to remove it.

---

<p align="center"><sub>
<b>Installation</b> · <a href="usage.md">Using the app</a> · <a href="command-line.md">Command line</a> · <a href="rules.md">Rules</a> · <a href="how-it-works.md">How it works</a> · <a href="excel-and-pdf.md">Excel &amp; PDF</a> · <a href="data-and-privacy.md">Data &amp; privacy</a> · <a href="limitations.md">Limitations</a> · <a href="development.md">Development</a><br>
<a href="../README.md">← Back to Zutritt</a>
</sub></p>
