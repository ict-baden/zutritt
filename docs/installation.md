<sub><a href="../README.md">Zutritt</a> › <a href="../README.md#documentation">Documentation</a> › Installation &amp; rights</sub>

# Installation & rights

Which file to download, how to check it, how to roll it out, and which rights the scanning account needs.

**Contents:** [Requirements](#requirements) · [Downloads](#downloads) · [Verifying the download](#verifying-the-download) · [MSI installer](#msi-installer) · [Deploying with Intune or Group Policy](#deploying-with-intune-or-group-policy) · [Single exe and ZIP](#single-exe-and-zip) · [Rights for scanning](#rights-for-scanning) · [Recommended setup](#recommended-setup) · [Updates and uninstalling](#updates-and-uninstalling) · [Supported versions](#supported-versions)

> [!IMPORTANT]
> **Pre-release, not code-signed yet.** Zutritt is built and tested in GitHub Actions on every push to `main`, but there is no tagged version yet. Program and MSI are signed as soon as Artifact Signing is set up for ICT-Baden; until then Windows SmartScreen may warn when you start them. Check the files against `SHA256SUMS.txt` – see [Verifying the download](#verifying-the-download).

## Requirements

| Topic | Requirement |
|---|---|
| **Operating system** | 64-bit Windows 10 version 1809 (build 17763) or later, or Windows 11. Builds are produced for `win-x64` only. |
| **.NET** | None for the MSI and for `Zutritt.exe` – both carry the .NET 10 runtime. The ZIP variant needs the **.NET 10 Desktop Runtime**. |
| **Network** | The scanning PC must reach the file servers over SMB. Shares, share permissions, local accounts and account names are queried with the standard Windows network APIs (`NetShareEnum`, `NetShareGetInfo`, `NetLocalGroupGetMembers`, LSA lookups). |
| **Active Directory** | Optional. Domain accounts are resolved and domain groups expanded only when the scanning PC is a **member of the domain** and a domain controller is reachable. |
| **Language** | English, German, Spanish, French or Chinese (simplified) for the window, the command-line output and the reports – following the Windows display language, otherwise English. Switch on the *Info* page, preset for all users in `%ProgramData%\Zutritt\settings.json` (`{ "language": "de" }`) or force with the environment variable `ZUTRITT_LANGUAGE` ([Using the app › Languages](usage.md#languages)). |

**Why Windows only.** What Zutritt reads – NTFS permissions, SMB share permissions, the local account databases of servers (SAM, LSA) and Active Directory – exists only on Windows and is only reachable through Windows APIs. The program is therefore a Windows (WPF) application. Its analysis core – rules, effective access, comparison, storage and reports – is platform-neutral and tested on Linux as well ([Development › Architecture](development.md#architecture)).

## Downloads

**Releases.** Each **tagged version** (`v0.1.0`, `v1.2.0` …) gets its own release – the [current one](https://github.com/ict-baden/zutritt/releases/latest) is what to install. Its file names contain the version (see below); `Zutritt.exe` and `SHA256SUMS.txt` of the current version are always at [`releases/latest/download/Zutritt.exe`](https://github.com/ict-baden/zutritt/releases/latest/download/Zutritt.exe) and [`…/SHA256SUMS.txt`](https://github.com/ict-baden/zutritt/releases/latest/download/SHA256SUMS.txt).

**[latest](https://github.com/ict-baden/zutritt/releases/tag/latest)** is the newest build of `main`, updated in place on every push and marked as a *pre-release*. Its file names never change, so these links always give the newest build:

| File | Runtime | Use it for |
|---|---|---|
| [`Zutritt-x64.msi`](https://github.com/ict-baden/zutritt/releases/download/latest/Zutritt-x64.msi) | included | Installing for all users of a PC, also through Intune or Group Policy. |
| [`Zutritt.exe`](https://github.com/ict-baden/zutritt/releases/download/latest/Zutritt.exe) | included | Running without installation, e.g. from an admin share or a USB stick. |
| [`Zutritt-win-x64-framework.zip`](https://github.com/ict-baden/zutritt/releases/download/latest/Zutritt-win-x64-framework.zip) | .NET 10 Desktop Runtime | A small download where the runtime is already installed. |
| [`SHA256SUMS.txt`](https://github.com/ict-baden/zutritt/releases/download/latest/SHA256SUMS.txt) | – | SHA-256 checksums of all files in the release. |
| [`Zutritt-sbom.cdx.json`](https://github.com/ict-baden/zutritt/releases/download/latest/Zutritt-sbom.cdx.json) | – | Bill of materials (CycloneDX): every library the program contains, and the .NET and Windows Desktop runtime packs with their versions. |

Builds from `main` carry versions like `0.1.0-ci.12` (12 = the number of the build run). A **tagged version** `v1.2.0` gets its own release whose file names contain the version: `Zutritt-1.2.0-x64.msi`, `Zutritt-1.2.0-win-x64-framework.zip`, `Zutritt-1.2.0-sbom.cdx.json`, plus `Zutritt.exe` and `SHA256SUMS.txt`. Tags with a hyphen (`v1.2.0-rc1`) are marked as pre-releases.

Every variant contains the licence texts: `LICENSE` and `THIRD-PARTY-NOTICES.txt` lie next to the program in the ZIP and in the installation folder; `Zutritt.exe` carries them inside and shows them on the *Info* page and with `Zutritt.exe --lizenzen`.

## Verifying the download

Each release has a `SHA256SUMS.txt` with one line per file: the checksum and the file name. Compare the checksum of your download with that line:

```powershell
(Get-FileHash .\Zutritt-x64.msi -Algorithm SHA256).Hash.ToLower()
Select-String Zutritt-x64.msi .\SHA256SUMS.txt
```

Or check every file of the release that is in the current folder:

```powershell
Get-Content .\SHA256SUMS.txt | ForEach-Object {
    $hash, $name = $_ -split '\s+', 2
    if (Test-Path $name) {
        $ok = (Get-FileHash $name -Algorithm SHA256).Hash -eq $hash
        '{0}  {1}' -f $(if ($ok) { 'OK  ' } else { 'FAIL' }), $name
    }
}
```

On Linux or macOS: `sha256sum -c SHA256SUMS.txt --ignore-missing`.

**Signatures.** Once code signing is active, `Get-AuthenticodeSignature .\Zutritt-x64.msi` shows `Valid` and the signer, and SmartScreen no longer warns. Until then it shows `NotSigned`, and SmartScreen may show *Windows protected your PC* when you start `Zutritt.exe` or the MSI: check the checksum, then *More info* › *Run anyway*. The release notes of `latest` say whether a build is signed.

## MSI installer

The MSI installs per machine (administrator rights needed):

- program files into `C:\Program Files\Zutritt`, together with `LICENSE` and `THIRD-PARTY-NOTICES.txt`,
- one Start menu entry *Zutritt*,
- the registry value `HKLM\SOFTWARE\ICT-Baden\Zutritt\StartMenuShortcut` that Windows Installer needs to track the Start menu entry,
- **no** service, **no** autostart, no scheduled task, no changes to other settings.

```powershell
# As administrator
msiexec /i Zutritt-x64.msi                   # interactive
msiexec /i Zutritt-x64.msi /qn               # silent, e.g. for software distribution
msiexec /i Zutritt-x64.msi /qn /l*v msi.log  # silent with a log file
msiexec /x Zutritt-x64.msi /qn               # uninstall
```

The installer closes a running Zutritt before it replaces the files. *Apps & features* links to [SUPPORT.md](../SUPPORT.md) for help.

## Deploying with Intune or Group Policy

- **Intune:** add `Zutritt-x64.msi` as a *line-of-business app* (Intune reads product code and version from the MSI), or package it with the *Microsoft Win32 Content Prep Tool* as a *Windows app (Win32)* with the install command `msiexec /i Zutritt-x64.msi /qn` and an MSI detection rule. Assign it to devices – the MSI installs per machine.
- **Group Policy:** put the MSI on a share that the computer accounts can read and assign it under *Computer Configuration › Policies › Software Settings › Software installation*. It is installed at the next start of the PC.
- **Updates:** every build has its own product code and replaces the previous one (major upgrade, see [Updates](#updates-and-uninstalling)). Deploy the new MSI and let it supersede the old one; there is no need to uninstall first.

Zutritt only needs to be installed on the PCs that scan – usually a few admin workstations, not the file servers.

## Single exe and ZIP

**`Zutritt.exe`** is the whole program in one file – runtime and native libraries (SQLite, WPF) are embedded. Copy it anywhere and start it. At the first start .NET unpacks the native libraries (about 10 MB) into `%TEMP%\.net\Zutritt\…` and leaves them there for later starts; set `DOTNET_BUNDLE_EXTRACT_BASE_DIR` to use another folder. Where AppLocker or WDAC block programs in `%TEMP%`, use the MSI or the ZIP instead. While it is not signed, SmartScreen may show *Windows protected your PC*; *More info* › *Run anyway* starts it.

**The ZIP** contains a smaller, framework-dependent build, `LICENSE`, `THIRD-PARTY-NOTICES.txt` and a copy of the project's `README.md` (its images and links point into the repository and only work on GitHub). Extract it and start `Zutritt.exe`; Windows asks for the .NET 10 Desktop Runtime if it is missing.

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

A server, directory or account database that does not answer at all does not stop the run: after a time limit it is treated as unreachable ([Limitations › Scanning](limitations.md#scanning)).

**Backup privilege.** Zutritt opens every object with backup intent (`FILE_OPEN_FOR_BACKUP_INTENT`). If the backup privilege (*SeBackupPrivilege*) is already **enabled** in the process token, Windows then lets Zutritt read the permissions regardless of the ACL. Zutritt does not enable the privilege itself; whether it is enabled depends on how the process was started.

> [!TIP]
> Unreadable areas are never silently skipped: they appear as ZT09 or ZT13 findings, in the tile *nicht lesbar* on the overview, on the *Fehler* sheet of the Excel report and under *Nicht geprüfte Bereiche* in the PDF.

## Recommended setup

- **Scan from an admin workstation** with an account that is a local administrator on the file servers you scan. This gives Zutritt the share permissions (level 502) and the servers' local groups.
- **For local folders** start Zutritt *as administrator*; otherwise folders that only administrators may read show up as unreadable.
- **On a domain-joined PC**, so that domain groups are expanded down to people. In a workgroup only local accounts are resolved.
- **For scheduled runs**, create the task under that same account and enable *Run with highest privileges* if local folders are included. The task's account also determines where the run history is stored – see [Command line › Scheduled tasks](command-line.md#scheduled-tasks).

## Updates and uninstalling

- **MSI:** install the newer MSI over the old one. Builds from `main` replace each other (Windows Installer compares only the first three parts of the version, and the package allows same-version upgrades); a release with a higher version replaces them as well. Installing a version whose first three parts are *lower* is refused (*Eine neuere Version von Zutritt ist bereits installiert.*). Builds with the same first three parts – the builds from `main` and the tagged release of that version – replace each other in **either** direction, so check the version under *Info* (the file version of `Zutritt.exe` carries the build number as its fourth part).
- **Exe and ZIP:** replace the files.
- **Database:** a newer Zutritt upgrades the database schema automatically. An older Zutritt refuses to open a database written by a newer version instead of damaging it.
- **Uninstalling** removes the program files, the Start menu entry and the registry value. The run history and the logs in `%LocalAppData%\Zutritt` of each user stay; delete that folder to remove them.

Zutritt does not check for updates by itself. Watch the [releases](https://github.com/ict-baden/zutritt/releases) on GitHub (*Watch › Custom › Releases*) to hear about new versions.

## Supported versions

Security fixes are provided for the **newest released version** only – install it to get them ([SECURITY.md](../SECURITY.md)). Builds of `main` on `latest` are always the newest state but are not a release with a version promise. Help is available for free on GitHub and with guaranteed response times under a support contract ([SUPPORT.md](../SUPPORT.md)).

---

<p align="center"><sub>
<b>Installation</b> · <a href="usage.md">Using the app</a> · <a href="command-line.md">Command line</a> · <a href="rules.md">Rules</a> · <a href="how-it-works.md">How it works</a> · <a href="excel-and-pdf.md">Excel &amp; PDF</a> · <a href="data-and-privacy.md">Data &amp; privacy</a> · <a href="limitations.md">Limitations</a> · <a href="development.md">Development</a><br>
<a href="../README.md">← Back to Zutritt</a>
</sub></p>
