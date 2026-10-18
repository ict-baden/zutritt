<sub><a href="../README.md">Zutritt</a> › <a href="../README.md#documentation">Documentation</a> › Limitations</sub>

# Limitations

What Zutritt cannot see or decide, stated plainly. Where it has to guess, it errs on the side of **showing more access, not less** – an audit must not miss anything.

**Contents:** [Status](#status) · [Rights of the scanning account](#rights-of-the-scanning-account) · [Identities and groups](#identities-and-groups) · [When in doubt, more access](#when-in-doubt-more-access) · [The access model](#the-access-model) · [Scanning](#scanning) · [Comparison](#comparison) · [Reports](#reports) · [Command line](#command-line)

## Status

- **Pre-release.** Builds of `main` are published as the release [`latest`](https://github.com/ict-baden/zutritt/releases/tag/latest); there is no tagged version yet.
- **Not code-signed yet.** Signing starts once Artifact Signing is set up for ICT-Baden; until then SmartScreen may warn about `Zutritt.exe` and the MSI. Every release has `SHA256SUMS.txt` to check the files ([Installation › Verifying the download](installation.md#verifying-the-download)).
- **German only.** User interface, command-line output and reports are German. A multilingual interface (English, German, Spanish, French, Chinese) is planned for version 1.0.
- **Windows x64 only, by design**, Windows 10 1809 or later. The task itself is bound to Windows: NTFS permissions, SMB share permissions (`NetShareEnum`, `NetShareGetInfo`), the local account databases of servers (SAM, LSA) and Active Directory are Windows concepts with Windows-only APIs. The Windows-specific code is kept in `Zutritt.Platform.Windows`; the analysis core `Zutritt.Core` – rules, effective access, comparison, storage, reports – is platform-neutral and its tests also run on Linux in CI. A front end on another platform over stored runs would therefore be possible; scanning stays on Windows.
- **Security fixes** are provided for the newest released version only ([SECURITY.md](../SECURITY.md)).

## Rights of the scanning account

Zutritt reports what the scanning account can read ([details](installation.md#rights-for-scanning)).

- A folder whose permissions or contents cannot be read becomes [ZT09](rules.md#zt09--folder-not-readable). If its contents cannot be listed, **nothing below it is assessed**; if it can still be listed, its subfolders are scanned and assessed individually.
- Without administrator rights on a server, the share permissions are unknown ([ZT13](rules.md#zt13--share-permissions-not-readable)); access over the network is then rated by NTFS alone, which can be more than actually possible.
- Zutritt does not elevate itself. Local folders that only administrators may read need *Run as administrator*.

## Identities and groups

- **Active Directory only for the scanning PC's own domain.** Domain groups are expanded only when the PC is domain-joined and a domain controller answers. Accounts of **trusted domains or forests** are named through Windows (LSA), but their groups are **not expanded** – their members do not appear as people.
- **AD lookups are tested against a simulated directory only.** The development machine has no domain; the AD code is covered by tests with a fake directory and by tests for an unreachable domain, but not yet against a real domain controller.
- **Remote SAM restrictions.** Current Windows versions allow remote queries of the local account database only for administrators by default (policy *RestrictRemoteSAM*). Without them, members of a server's local and *VORDEFINIERT* groups are missing, and whether a local account is **disabled** is unknown – so [ZT12](rules.md#zt12--disabled-account-has-rights) cannot fire for it. Local accounts are still named through the server's LSA; they stay *Unbekannt* only if that lookup fails too.
- **Same BUILTIN group on several servers.** *VORDEFINIERT\\Administratoren* has the same SID on every server but different members. A run can hold each SID only once, so when one run covers several servers, the members of all servers are **merged** and a note is added. Access through such a group may then apply on only one of the servers. Scan servers separately where this matters.
- **Nesting deeper than 20 levels** is not expanded further; the group is marked incomplete.
- **Entra ID accounts** (`S-1-12-…`) can only be named if they have signed in on the machine; otherwise they stay *Unbekannt*.
- **The people view** lists accounts that appear in the run – in an ACL or in a resolved group. Someone who only gets access through *Jeder* or *Authentifizierte Benutzer* is not listed individually; the folder page says that practically everyone can get in.

## When in doubt, more access

These decisions are deliberate:

- **Conditional ACEs** (dynamic access control): conditional allow entries count as allow, conditional and object-specific deny entries are ignored – the condition cannot be evaluated offline, and counting a deny could hide access.
- **Partial share denies** – a deny that blocks no share level completely, e.g. only *delete* – are ignored.
- **Share levels** are the three levels of the Windows share dialog. A share entry that grants writing without reading cannot be expressed and counts as *Ändern*.
- **Unreadable share permissions** mean NTFS alone, an upper bound.
- **Several shares on the same folder:** the one with the widest access is used.
- **Orphaned vs. unknown:** a SID is only called orphaned when the responsible authority said so; otherwise it stays *Unbekannt* – Zutritt prefers no hint over a wrong deletion hint.

## The access model

- **The owner's implicit rights** (reading and changing permissions) are not part of the effective right. Personal owners are reported by [ZT10](rules.md#zt10--owner-is-a-user-account) instead.
- **Privileges** that let administrators bypass ACLs – backup, restore, take ownership – are not part of the calculation; it is based on ACLs and group memberships.
- **Local folders under a share** are evaluated as access over that share (with *NETZWERK* in the token and the share permissions applied). Access at the console or over Remote Desktop, without the share, is not shown separately.
- **CREATOR OWNER on derived folders.** Where Zutritt derives a folder's rights from its nearest stored ancestor – for a comparison, when the folder was stored in only one of the runs – *ERSTELLER-BESITZER* entries are not turned into rights for the creator, because the creator is unknown. Folders where CREATOR OWNER actually produced an entry are stored during the scan and are not affected.
- **ZT14 tolerance.** One account with exactly the CREATOR OWNER rights is accepted as the original creator. A moved folder whose only foreign entry happens to have exactly those rights is therefore not detected.

## Scanning

- **Files** are only examined with `--dateien` / *Dateien einbeziehen*, and only stored when they have permissions of their own.
- **Junctions and symbolic links** are recorded but not followed. What they point to is only scanned if it is a target itself or lies under one.
- **Depth is relative to the target.** [ZT06](rules.md#zt06--deeply-hidden-exception) counts levels below the scanned target; a deeper target finds fewer “deep” exceptions.
- **One folder after another.** A scan reads the tree sequentially; large servers take time accordingly.
- **Time limits for sources that do not answer.** A directory (Active Directory, the account database of a server) or an LSA account lookup that does not answer within **60 seconds** is not asked again in that run: its accounts stay *Unbekannt* and the run gets a note (*… antwortet nicht innerhalb von 60 Sekunden …*). Listing the shares of a server and reading the details of a share get **2 minutes**; after that the server or share counts as unreadable and the run continues with the other targets. A call that timed out keeps running in the background until Windows gives up; its late answer is discarded.

## Comparison

- **Deleted or no longer special?** A folder that is no longer stored appears as *Eigene Rechte entfallen* (app) or *Keine eigenen Rechte mehr* (reports) – whether it was deleted or its exception was removed cannot be told apart.
- **Renamed or moved folders** are matched by path, so they appear as one removed and one new folder.
- **Different targets.** By default only runs with the same targets are compared. If you choose another run on *Vergleich*, differences may come from the targets – the app warns about that.
- **Runs without group resolution** are compared without memberships on both sides.

## Reports

Very large runs are truncated in the reports with a visible notice – the PDF much earlier than the Excel workbook. The database always keeps the complete run. Limits: [Excel & PDF › Size limits](excel-and-pdf.md#size-limits-and-truncation).

## Command line

- **Unreachable targets do not change the exit code** – they become *Info* findings. A run that could read nothing can end with exit code 0 ([details](command-line.md#exit-codes)).
- **`> NUL`** does not silence the output; it still appears in the console.
- **PowerShell 7.4+** writes the console's code page into files with `> log.txt`; use a `.cmd` file for logs ([details](command-line.md#logs-and-encoding)).
- **A Windows program:** command prompts do not wait for it unless you use `start /wait` or `| Out-Default`.

---

<p align="center"><sub>
<a href="installation.md">Installation</a> · <a href="usage.md">Using the app</a> · <a href="command-line.md">Command line</a> · <a href="rules.md">Rules</a> · <a href="how-it-works.md">How it works</a> · <a href="excel-and-pdf.md">Excel &amp; PDF</a> · <a href="data-and-privacy.md">Data &amp; privacy</a> · <b>Limitations</b> · <a href="development.md">Development</a><br>
<a href="../README.md">← Back to Zutritt</a>
</sub></p>
