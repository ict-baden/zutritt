<sub><a href="../README.md">Zutritt</a> › <a href="../README.md#documentation">Documentation</a> › Rules reference</sub>

# Rules reference

The 15 rules (*Prüfregeln*) Zutritt evaluates on every run: what each finding means, why it matters, what to do about it – and when it is deliberately not reported. The rule IDs are stable across versions, so findings can be compared between runs.

**Contents:** [Overview](#overview) · [How findings are produced](#how-findings-are-produced) · [ZT01](#zt01--broad-group-can-write) · [ZT02](#zt02--broad-group-can-read) · [ZT03](#zt03--right-granted-directly-to-a-person) · [ZT04](#zt04--orphaned-sid) · [ZT05](#zt05--full-control-for-non-admins) · [ZT06](#zt06--deeply-hidden-exception) · [ZT07](#zt07--deny-entry) · [ZT08](#zt08--share-everyone-can-change) · [ZT09](#zt09--folder-not-readable) · [ZT10](#zt10--owner-is-a-user-account) · [ZT11](#zt11--empty-group-has-rights) · [ZT12](#zt12--disabled-account-has-rights) · [ZT13](#zt13--share-permissions-not-readable) · [ZT14](#zt14--permissions-differ-from-inheritance) · [ZT15](#zt15--entries-not-in-canonical-order)

## Overview

| ID | Severity | English | German title in the app and reports | Looks at |
|---|---|---|---|---|
| ZT01 | ![High](https://img.shields.io/badge/High-C62828) | Broad group can write | *Breite Gruppe darf schreiben* | explicit NTFS entries |
| ZT02 | ![Medium](https://img.shields.io/badge/Medium-EF6C00) | Broad group can read | *Breite Gruppe darf lesen* | explicit NTFS entries |
| ZT03 | ![Medium](https://img.shields.io/badge/Medium-EF6C00) | Right granted directly to a person | *Recht direkt an Person vergeben* | explicit NTFS entries |
| ZT04 | ![Medium](https://img.shields.io/badge/Medium-EF6C00) | Orphaned SID | *Verwaiste SID* | entries, owner, share permissions |
| ZT05 | ![High](https://img.shields.io/badge/High-C62828) | Full control for non-admins | *Vollzugriff für Nicht-Admins* | explicit NTFS entries |
| ZT06 | ![Low](https://img.shields.io/badge/Low-FBC02D) | Deeply hidden exception | *Tief versteckte Ausnahme* | broken inheritance |
| ZT07 | ![Info](https://img.shields.io/badge/Info-BDBDBD) | Deny entry | *Verweigern-Eintrag* | explicit NTFS entries |
| ZT08 | ![Info](https://img.shields.io/badge/Info-BDBDBD) | Share: everyone can change | *Freigabe: alle dürfen ändern* | share permissions |
| ZT09 | ![Info](https://img.shields.io/badge/Info-BDBDBD) | Folder not readable | *Ordner nicht lesbar* | read errors |
| ZT10 | ![Low](https://img.shields.io/badge/Low-FBC02D) | Owner is a user account | *Besitzer ist ein Benutzerkonto* | owner |
| ZT11 | ![Low](https://img.shields.io/badge/Low-FBC02D) | Empty group has rights | *Leere Gruppe berechtigt* | explicit NTFS entries |
| ZT12 | ![Medium](https://img.shields.io/badge/Medium-EF6C00) | Disabled account has rights | *Deaktiviertes Konto berechtigt* | explicit NTFS entries |
| ZT13 | ![Info](https://img.shields.io/badge/Info-BDBDBD) | Share permissions not readable | *Freigabe-Berechtigungen nicht lesbar* | share permissions |
| ZT14 | ![Low](https://img.shields.io/badge/Low-FBC02D) | Permissions differ from inheritance | *Rechte weichen von der Vererbung ab* | inherited NTFS entries |
| ZT15 | ![Medium](https://img.shields.io/badge/Medium-EF6C00) | Entries not in canonical order | *Einträge nicht in kanonischer Reihenfolge* | order of NTFS and share entries |

**Severities** – *Hoch* (high), *Mittel* (medium), *Niedrig* (low), *Info* – order the lists, colour the reports and set the [exit code](command-line.md#exit-codes) of `scan`: any *Hoch* gives 2, any *Mittel* gives 1, *Niedrig* and *Info* alone give 0.

## How findings are produced

- **Where the entry is set.** NTFS findings come from **explicit** (not inherited) entries only. A finding appears on the folder where the entry is set, not again on each of the thousands of folders that inherit it.
- **One finding per rule, path and account.** This combination is the finding's key; the comparison uses it to decide what is new and what is resolved.
- **Per scope.** An entry can apply to the folder itself, to its subfolders, or only to its files. Zutritt evaluates these scopes separately, so rights from different scopes never add up to a right that exists nowhere. If the right only reaches the contents, the message says *(gilt für Unterordner bzw. Dateien)* (applies to subfolders or files).
- **After deny entries.** For ZT01, ZT02 and ZT05 the right counts after explicit deny entries on the same folder that hit the account or its members – in stored order, like Windows: a deny only blocks what no entry before it has allowed (an inherited deny counts too if it stands before the explicit allow, see ZT15). *Jeder: Ändern* together with an explicit deny of writing for *Jeder* placed before it (the canonical order) is therefore not a ZT01; placed after it, it blocks nothing, and the folder gets ZT01 and ZT15.
- **Rights levels.** Messages name the level – *Auflisten*, *Lesen*, *Schreiben*, *Ändern*, *Vollzugriff* – see [rights levels](how-it-works.md#rights-levels). Deny entries name the individual rights that are denied (e.g. *„Löschen“ verweigert*), because a level would be misleading there.
- **Findings are stored with the run.** Reports show the findings as they were evaluated at scan time; they are not re-evaluated later.

### Groups and accounts the rules refer to

| Term | Accounts |
|---|---|
| **Broad group** | *Jeder* (Everyone), *Authentifizierte Benutzer* (Authenticated Users), *VORDEFINIERT\\Benutzer* (BUILTIN\\Users), *VORDEFINIERT\\Gäste* (Guests), *Domänen-Benutzer* (Domain Users, RID 513), *Domänen-Gäste* (Domain Guests, RID 514), *INTERAKTIV* and *NETZWERK* |
| **Administrator-like** | *SYSTEM*, *Administratoren*, *Domänen-Admins* (RID 512), *Organisations-Admins* (RID 519), *TrustedInstaller*, *Sicherungs-Operatoren* (Backup Operators) and the placeholder *ERSTELLER-BESITZER* (CREATOR OWNER) |
| **Not a person** | Service SIDs (*NT SERVICE\\…*), virtual accounts (IIS application pools, virtual machines, drivers, scheduled tasks, *Window Manager\\DWM-1* …), app packages and capabilities, integrity levels, *SYSTEM*, *LOKALER DIENST*, *NETZWERKDIENST*. Windows sometimes reports virtual accounts as users; Zutritt never treats them as people. |

### Windows-managed folders

A folder counts as managed by Windows when its owner is *TrustedInstaller* or it has an explicit entry for an identity only Windows itself assigns: app packages (`S-1-15-2-…`), capabilities (`S-1-15-3-…`) and capability groups, *TrustedInstaller*, *EINGESCHRÄNKT* (RESTRICTED) or *SCHREIBGESCHÜTZT* (WRITE RESTRICTED). That “users may read `C:\Program Files`” is intended, and nobody should change permissions there anyway. In such folders:

- **not reported:** ZT02, ZT03, ZT06, ZT10, ZT14, and ZT05 for the folder's own owner;
- **still reported:** ZT01, ZT05 for anyone else, ZT04, ZT07, ZT11, ZT12, ZT15.

These exceptions were developed against `C:\Program Files`, `C:\ProgramData`, `C:\Users` and `C:\Windows\System32`.

---

### ZT01 – Broad group can write

![High](https://img.shields.io/badge/High-C62828) &nbsp; *Breite Gruppe darf schreiben*

**What it means.** A group that comprises practically everyone (e.g. *Jeder*, *Authentifizierte Benutzer*, *Domänen-Benutzer*) may create or change files here. Any signed-in person can falsify or delete data – or encrypt it, for example through malware.

**What to do.** Remove the broad group's write right and give it only to a functional group that contains exactly the responsible people.

**When it fires.** A [broad group](#groups-and-accounts-the-rules-refer-to) has an explicit allow entry whose right, after explicit deny entries, reaches at least *Schreiben* (write) on the folder, its subfolders or its files. Also in Windows-managed folders.

**Example.** *„Jeder“ hat „Ändern“ – praktisch jede angemeldete Person darf hier schreiben.*

<details><summary>Text in the app and reports</summary>

> Eine Gruppe, die praktisch alle Personen umfasst (z. B. „Jeder“, „Authentifizierte Benutzer“, „Domänen-Benutzer“), darf hier Dateien anlegen oder ändern. Damit kann jede angemeldete Person Daten verfälschen, löschen oder – etwa durch Schadsoftware – verschlüsseln.
>
> **Empfehlung:** Schreibrecht der breiten Gruppe entfernen und nur einer fachlichen Gruppe geben, die genau die zuständigen Personen enthält.

</details>

### ZT02 – Broad group can read

![Medium](https://img.shields.io/badge/Medium-EF6C00) &nbsp; *Breite Gruppe darf lesen*

**What it means.** A group that comprises practically everyone may read here or list the contents. Confidential data is visible to every signed-in person.

**What to do.** Check whether the data really is meant for everyone; otherwise replace the broad group with a functional group.

**When it fires.** A broad group has an explicit allow entry whose right reaches *Lesen* (read data; for folders: list contents) but not *Schreiben* – write access is ZT01.

**Not reported.** Below *Lesen*: merely traversing a folder or reading attributes – such as *Jeder: Ordner durchsuchen* on share roots or on the locked compatibility links of Windows – shows nobody any data. Not in Windows-managed folders.

**Example.** *„VORDEFINIERT\\Benutzer“ hat „Lesen“ – praktisch jede angemeldete Person darf hier lesen.*

<details><summary>Text in the app and reports</summary>

> Eine Gruppe, die praktisch alle Personen umfasst, darf hier lesen oder den Inhalt auflisten. Vertrauliche Daten sind damit für jede angemeldete Person sichtbar.
>
> **Empfehlung:** Prüfen, ob die Daten wirklich für alle bestimmt sind; sonst die breite Gruppe durch eine fachliche Gruppe ersetzen.

</details>

### ZT03 – Right granted directly to a person

![Medium](https://img.shields.io/badge/Medium-EF6C00) &nbsp; *Recht direkt an Person vergeben*

**What it means.** The right is given to an individual user account instead of a group. Such entries are easily forgotten when someone changes department or leaves, and they make the permission structure hard to follow.

**What to do.** Grant the right through a group (role or department) and remove the entry for the person.

**When it fires.** An explicit allow entry for a user account that belongs to a person.

**Not reported.** For the built-in *Administrator* account (RID 500), for [accounts that are not people](#groups-and-accounts-the-rules-refer-to), and in Windows-managed folders.

**Example.** *„MUSTER\\qc.leitung“ hat „Vollzugriff“ direkt als Person statt über eine Gruppe.*

<details><summary>Text in the app and reports</summary>

> Das Recht ist einem einzelnen Benutzerkonto statt einer Gruppe gegeben. Solche Einträge werden bei Wechsel der Abteilung oder beim Austritt leicht vergessen und machen die Rechtevergabe unübersichtlich.
>
> **Empfehlung:** Recht über eine Gruppe (Rolle bzw. Abteilung) vergeben und den Eintrag für die Person entfernen.

</details>

### ZT04 – Orphaned SID

![Medium](https://img.shields.io/badge/Medium-EF6C00) &nbsp; *Verwaiste SID*

**What it means.** An entry or the owner refers to an account that no longer exists. The SID cannot be mapped to a name; nobody can easily tell to whom the right was once given.

**What to do.** Remove the orphaned entry; if the SID is the owner, set a valid owner (e.g. *Administratoren*).

**When it fires.** An explicit allow or deny entry, the owner of a folder, or an entry in the share permissions refers to a SID that is [orphaned](how-it-works.md#identities): the account database responsible for it was reachable and does not know it.

**Not reported.** For SIDs that merely cannot be checked – domain unreachable, server not asked, Entra ID accounts (`S-1-12-…`): they stay *Unbekannt* (unknown), because a wrong “orphaned” would invite deleting valid permissions. Never for SIDs that are not accounts: services not installed on this machine, app capabilities, app packages, integrity levels, logon sessions, virtual accounts, and *VORDEFINIERT* groups that exist only on servers.

**Example.** *Die verwaiste SID S-1-5-21-…-1107 hat einen Eintrag „Ändern“ – das Konto gibt es nicht mehr.*

<details><summary>Text in the app and reports</summary>

> Ein Eintrag oder der Besitzer verweist auf ein Konto, das es nicht mehr gibt. Die SID lässt sich keinem Namen zuordnen; niemand kann ohne Weiteres sagen, wem das Recht einmal gegeben wurde.
>
> **Empfehlung:** Verwaisten Eintrag entfernen; ist die SID Besitzer, einen gültigen Besitzer (z. B. „Administratoren“) eintragen.

</details>

### ZT05 – Full control for non-admins

![High](https://img.shields.io/badge/High-C62828) &nbsp; *Vollzugriff für Nicht-Admins*

**What it means.** An account or group without an administrator role has full control. It may change permissions and take ownership – that is, give itself and others any right, bypassing every review.

**What to do.** Reduce to *Ändern* (modify); give full control only to administrators and *SYSTEM*.

**When it fires.** An explicit allow entry gives a user, group, computer or unresolvable account *Vollzugriff* after deny entries. A right counts as full control as soon as it includes *Berechtigungen ändern* (change permissions) or *Besitz übernehmen* (take ownership).

**Not reported.** For [administrator-like accounts](#groups-and-accounts-the-rules-refer-to), the built-in *Administrator*, broad groups (that is ZT01) and special identities such as services. In Windows-managed folders not for the owner: Windows gives the owner full control there itself (e.g. on their error reports).

**Example.** *„MUSTER\\QA-Team“ hat Vollzugriff und darf damit Berechtigungen ändern und den Besitz übernehmen.*

<details><summary>Text in the app and reports</summary>

> Ein Konto oder eine Gruppe ohne Administratorrolle hat Vollzugriff. Damit darf es Berechtigungen ändern und den Besitz übernehmen – also sich und anderen beliebige Rechte geben, an jeder Prüfung vorbei.
>
> **Empfehlung:** Auf „Ändern“ reduzieren; Vollzugriff nur Administratoren und SYSTEM geben.

</details>

### ZT06 – Deeply hidden exception

![Low](https://img.shields.io/badge/Low-FBC02D) &nbsp; *Tief versteckte Ausnahme*

**What it means.** Inheritance is interrupted deep in the folder structure. Such exceptions cannot be seen from above; changes to parent folders do not arrive here.

**What to do.** Check whether the exception is still needed and otherwise re-enable inheritance. Document necessary exceptions, or map them higher up in the structure.

**When it fires.** Inheritance is switched off on a folder at least 3 levels below the scan target (adjustable: *Ausnahme gilt als tief ab Ebene* in the app, `--ausnahme-ab N` on the command line), and its entries give access to someone other than administrators, *SYSTEM*, Windows or services. The depth counts from the target you scanned – the same folder is deeper in a scan of `\\server` than in a scan of `\\server\share\dept`.

**Not reported.** Exceptions that only shut others out – e.g. folders under `C:\ProgramData` that only *SYSTEM* and *Administratoren* may open – hide no access. Not in Windows-managed folders.

**Example.** *Vererbung in Tiefe 3 unterbrochen (ab Tiefe 3 gilt eine Ausnahme als versteckt).*

<details><summary>Text in the app and reports</summary>

> Tief in der Ordnerstruktur ist die Vererbung unterbrochen. Solche Ausnahmen sieht man von oben nicht; Änderungen an übergeordneten Ordnern kommen hier nicht an.
>
> **Empfehlung:** Prüfen, ob die Ausnahme noch nötig ist, und sonst die Vererbung wieder einschalten. Nötige Ausnahmen dokumentieren oder weiter oben in der Struktur abbilden.

</details>

### ZT07 – Deny entry

![Info](https://img.shields.io/badge/Info-BDBDBD) &nbsp; *Verweigern-Eintrag*

**What it means.** An explicit deny entry takes precedence over allow entries. Denials are hard to trace and often hit more people than intended (e.g. all members of a group).

**What to do.** Prefer not granting the right in the first place – tailor the groups instead. Use denials only for justified exceptions, and document them.

**When it fires.** An account or group has an explicit deny entry on the folder. The message names the denied rights.

**Not reported.** The deny entries Windows puts on its compatibility links (*Anwendungsdaten*, *Eigene Dateien*, *All Users* …): *Jeder* is denied *Ordner auflisten* for that folder only, to stop endless loops when browsing. They take nobody's data away and would only be noise. Conditional deny entries are ignored altogether – see [limitations](limitations.md#when-in-doubt-more-access).

**Example.** *Verweigern-Eintrag für „MUSTER\\Praktikanten“ („Löschen“ und „Unterordner und Dateien löschen“ verweigert).*

<details><summary>Text in the app and reports</summary>

> Ein expliziter Verweigern-Eintrag geht allen Erlauben-Einträgen vor. Verweigerungen sind schwer nachzuvollziehen und treffen oft mehr Personen als beabsichtigt (z. B. alle Mitglieder einer Gruppe).
>
> **Empfehlung:** Recht lieber gar nicht erst vergeben (Gruppen passend zuschneiden). Verweigerungen nur in begründeten Ausnahmen nutzen und dokumentieren.

</details>

### ZT08 – Share: everyone can change

![Info](https://img.shields.io/badge/Info-BDBDBD) &nbsp; *Freigabe: alle dürfen ändern* (until 0.1.0-ci builds: *Freigabe: Jeder Vollzugriff* – the rule has always fired from *Change* on, not only for *Full Control*)

**What it means.** The share permission allows all signed-in people at least *Ändern* (change). Protection then rests on the NTFS permissions alone. This is practice recommended by Microsoft, but it should be a deliberate choice.

**What to do.** Nothing, if the NTFS permissions are clean. Otherwise restrict the share permission as well.

**When it fires.** A share permission allows *Jeder* or *Authentifizierte Benutzer* at least *Ändern* – despite the title, not only full control. A share without any security descriptor counts as *Jeder: Vollzugriff*, because that is how Windows treats it.

**Example.** *Freigabe-Berechtigung „Jeder“: „Vollzugriff“ – der Schutz liegt allein bei NTFS.*

<details><summary>Text in the app and reports</summary>

> Die Freigabe-Berechtigung erlaubt allen angemeldeten Personen mindestens „Ändern“. Der Schutz liegt damit allein bei den NTFS-Rechten. Das ist von Microsoft empfohlene Praxis, sollte aber bewusst so gewählt sein.
>
> **Empfehlung:** Nichts zu tun, wenn die NTFS-Rechte sauber sind. Sonst die Freigabe-Berechtigung zusätzlich einschränken.

</details>

### ZT09 – Folder not readable

![Info](https://img.shields.io/badge/Info-BDBDBD) &nbsp; *Ordner nicht lesbar*

**What it means.** The folder (or file) could not be read, e.g. because access was denied. Its own permissions have not been checked in this run. If the folder could not even be listed, nothing below it was checked either; if only its permissions were unreadable, its subfolders were still scanned and evaluated.

**What to do.** Fix the cause, or scan with an account that may read the permissions, and repeat the run.

**When it fires.**

- The permissions of a folder or file could not be read (*Berechtigungen nicht lesbar*), its contents could not be listed (*Inhalt nicht auflistbar*), or its ACL contains entry types Zutritt cannot evaluate (*nicht vollständig auswertbar*).
- A target is unreachable or invalid, a server's list of shares is unreadable, or a server has no disk shares.
- A problem that is not tied to a folder: an unreachable domain or account database, or a group nested deeper than 20 levels. The “path” of the finding is then the server, domain or group, and the message starts with *Nicht geprüft:*.

**Why only *Info*.** It is not a risk in itself but a gap in the evidence. It does not raise the exit code – check it when you monitor scheduled runs ([see the warning](command-line.md#exit-codes)).

**Example.** *Ordner nicht lesbar: Berechtigungen nicht lesbar: Zugriff verweigert – Berechtigungen hier und darunter nicht geprüft.*

<details><summary>Text in the app and reports</summary>

> Der Ordner (oder die Datei) konnte nicht gelesen werden, z. B. weil der Zugriff verweigert wurde. Seine Berechtigungen und alles darunter sind in diesem Lauf nicht geprüft.
>
> **Empfehlung:** Ursache beheben oder mit einem Konto scannen, das die Berechtigungen lesen darf, und den Lauf wiederholen.

</details>

### ZT10 – Owner is a user account

![Low](https://img.shields.io/badge/Low-FBC02D) &nbsp; *Besitzer ist ein Benutzerkonto*

**What it means.** The owner can change the permissions at any time – regardless of what the list says. If an individual is the owner, they keep this ability even after a change of role.

**What to do.** Transfer ownership to *Administratoren* (or an admin group).

**When it fires.** The owner of a stored folder is a user account that belongs to a person. This covers the owner's implicit rights, which the [effective-access calculation](how-it-works.md#effective-access) deliberately does not count.

**Not reported.** For the built-in *Administrator*, for accounts that are not people, and in Windows-managed folders. An orphaned owner is ZT04.

**Example.** *Besitzer ist das Benutzerkonto „MUSTER\\labor.ma01“.*

<details><summary>Text in the app and reports</summary>

> Der Besitzer darf die Berechtigungen jederzeit ändern – unabhängig davon, was in der Liste steht. Ist eine einzelne Person Besitzer, behält sie diese Möglichkeit auch nach einem Rollenwechsel.
>
> **Empfehlung:** Besitz auf „Administratoren“ (bzw. eine Admin-Gruppe) übertragen.

</details>

### ZT11 – Empty group has rights

![Low](https://img.shields.io/badge/Low-FBC02D) &nbsp; *Leere Gruppe berechtigt*

**What it means.** A group without members has rights. The entry has no effect today, but becomes effective unnoticed as soon as someone is added to the group.

**What to do.** Remove the entry if the group is no longer needed; otherwise document its purpose.

**When it fires.** An explicit allow entry for a group whose member list was read completely and is empty.

**Not reported.** For *VORDEFINIERT* groups (*Hauptbenutzer*, *Sicherungs-Operatoren* …): Windows enters them in system folders itself, and being empty is their normal state. Not when members could not be read, and not in runs without group resolution.

**Example.** *Die Gruppe „MUSTER\\Projekt-Alt“ hat keine Mitglieder, ist aber berechtigt („Ändern“).*

<details><summary>Text in the app and reports</summary>

> Eine Gruppe ohne Mitglieder ist berechtigt. Der Eintrag wirkt heute nicht, wird aber unbemerkt wirksam, sobald jemand in die Gruppe aufgenommen wird.
>
> **Empfehlung:** Eintrag entfernen, wenn die Gruppe nicht mehr gebraucht wird; sonst den Zweck dokumentieren.

</details>

### ZT12 – Disabled account has rights

![Medium](https://img.shields.io/badge/Medium-EF6C00) &nbsp; *Deaktiviertes Konto berechtigt*

**What it means.** A disabled user account has its own right here. If the account is re-enabled, the right applies immediately.

**What to do.** Remove the entry; delete the accounts of people who have left once the retention period has passed.

**When it fires.** An explicit allow entry for a user account that is disabled in Active Directory or in the local account database. Also in Windows-managed folders.

**Not reported.** When the disabled state cannot be read – e.g. local accounts of a server that restricts remote SAM queries ([limitations](limitations.md#identities-and-groups)).

**Example.** *Das deaktivierte Konto „MUSTER\\ext.auditor“ hat hier „Lesen“.*

<details><summary>Text in the app and reports</summary>

> Ein deaktiviertes Benutzerkonto hat hier ein eigenes Recht. Wird das Konto wieder aktiviert, gilt das Recht sofort.
>
> **Empfehlung:** Eintrag entfernen; ausgeschiedene Konten nach Ablauf der Frist löschen.

</details>

### ZT13 – Share permissions not readable

![Info](https://img.shields.io/badge/Info-BDBDBD) &nbsp; *Freigabe-Berechtigungen nicht lesbar*

**What it means.** The permissions of the share could not be read. Access over the network is therefore calculated from the NTFS permissions alone and may in reality be lower.

**What to do.** Scan again with administrator rights on the server.

**When it fires.** Reading the share's details (`NetShareGetInfo` level 502) failed – usually for lack of administrator rights on the server – or its security descriptor is invalid or contains entry types Zutritt cannot evaluate. Also for administrative shares without their own descriptor: their effective default is internal to Windows, and Zutritt does not guess it.

**Example.** *Die Freigabe-Berechtigungen von \\\\FS01\\Labor konnten nicht gelesen werden.*

<details><summary>Text in the app and reports</summary>

> Die Berechtigungen der Freigabe konnten nicht gelesen werden. Die Rechte über das Netz sind deshalb allein aus den NTFS-Rechten berechnet und können in Wirklichkeit geringer sein.
>
> **Empfehlung:** Mit Administratorrechten auf dem Server erneut scannen.

</details>

### ZT14 – Permissions differ from inheritance

![Low](https://img.shields.io/badge/Low-FBC02D) &nbsp; *Rechte weichen von der Vererbung ab*

**What it means.** The folder (or file) has inherited entries that do not match its parent folder. Moved folders are the typical case: when a folder is moved within a drive, Windows takes the old inherited permissions along instead of adopting those of the new location. Permissions from the former location may then still apply – including for people who should have no access at the new location – and changes to the parent only arrive after the next propagation of permissions.

**What to do.** Check whether the differing permissions are still needed. Otherwise re-apply inheritance: in the advanced security settings, disable inheritance and remove the inherited entries, then enable it again right away – explicit entries are kept.

**When it fires.** During the scan, Zutritt computes what each folder (and, with *Dateien einbeziehen*, each file) should inherit from its parent under the inheritance rules of Windows, and compares it with the inherited entries actually present – see the [inheritance check](how-it-works.md#inheritance-check). The message names the accounts that do not come from the parent; a second variant says that the folder inherits nothing although inheritance is on – usually a program created it with its own permissions.

**Not reported.** For roots, folders with broken inheritance (that is ZT06), folders below an unreadable parent, Windows-managed folders, and folders with entries for Windows-internal identities – installers often create such folders under program and app directories without propagation, which is not a moved folder.

**Example.** *Geerbte Einträge passen nicht zum übergeordneten Ordner – vermutlich verschoben. „MUSTER\\Labor-Alle“ kommt nicht von dort, sondern vom früheren Ort.*

On the *Ordner* page such folders carry the marker *Vererbung weicht ab* – see the [screenshot](usage.md#a-moved-folder).

<details><summary>Text in the app and reports</summary>

> Der Ordner (oder die Datei) hat geerbte Einträge, die nicht zum übergeordneten Ordner passen. Typisch sind verschobene Ordner: Beim Verschieben innerhalb eines Laufwerks nimmt Windows die alten geerbten Rechte mit, statt die des neuen Ortes zu übernehmen. Hier gelten dann womöglich noch Rechte vom früheren Ort – auch für Personen, die am neuen Ort keinen Zugriff haben sollen –, und Änderungen am übergeordneten Ordner kommen erst nach der nächsten Weitergabe der Rechte an.
>
> **Empfehlung:** Prüfen, ob die abweichenden Rechte noch gebraucht werden. Sonst die Vererbung neu anwenden: in den erweiterten Sicherheitseinstellungen die Vererbung deaktivieren, dabei die geerbten Einträge entfernen und sie gleich wieder aktivieren – eigene Einträge bleiben erhalten.

</details>

---

### ZT15 – Entries not in canonical order

![Medium](https://img.shields.io/badge/Medium-EF6C00) &nbsp; *Einträge nicht in kanonischer Reihenfolge*

**What it means.** Windows checks the entries of an access list strictly in stored order. Windows itself (Explorer, icacls, `Set-Acl`) always writes them in canonical order – explicit deny, explicit allow, then inherited entries – so that a deny on the same level wins and an explicit entry beats an inherited one. Scripts working on the raw security descriptor, old tools and migration programs can leave a different order. Then an allow placed before a deny takes effect first, or an inherited deny placed before an explicit allow blocks it. What the entries seem to say and what Windows grants differ.

**What to do.** Open the folder's security settings in Explorer: Windows reports that the permissions are incorrectly ordered and offers to reorder them. Reordering can change who has access – check the result afterwards, for example with a new scan and a [comparison](usage.md).

**When it fires.** An explicit entry stands after an inherited one, or an explicit deny stands after an explicit allow – in NTFS permissions or, as a deny after an allow, in share permissions. The order of inherited entries among themselves (nearest parent first) cannot be checked from the list alone and does not count. Zutritt always evaluates the stored order, exactly like Windows – see [effective access](how-it-works.md#effective-access); this rule makes the unusual order visible. The finding names the first misplaced entry.

**Not reported.** Never suppressed – also in Windows-managed folders, because the order decides who has access.

**Example.** *Verweigern-Eintrag für „MUSTER\\Labor-02“ steht hinter einem Erlauben-Eintrag – Windows wertet die gespeicherte Reihenfolge aus, Erlaubnisse davor greifen zuerst.*

<details><summary>Text in the app and reports</summary>

> Windows prüft die Einträge einer Zugriffsliste streng in der gespeicherten Reihenfolge. Normalerweise hält Windows sie in kanonischer Reihenfolge – explizite Verweigerungen, explizite Erlaubnisse, dann geerbte Einträge –, damit auf derselben Ebene Verweigern gewinnt und ein expliziter Eintrag vor einem geerbten gilt. Diese Liste weicht davon ab (meist von einem Skript, einem alten Werkzeug oder einem Migrationsprogramm geschrieben). Dann kann ein Erlauben vor einem Verweigern greifen oder eine geerbte Verweigerung ein explizites Erlauben aushebeln. Zutritt wertet wie Windows die gespeicherte Reihenfolge aus – das Ergebnis kann von dem abweichen, was die Einträge auf den ersten Blick versprechen.
>
> **Empfehlung:** Die Sicherheitseinstellungen des Ordners im Explorer öffnen: Windows meldet, dass die Berechtigungen falsch angeordnet sind, und bietet an, sie neu anzuordnen. Das kann ändern, wer Zugriff hat – das Ergebnis danach prüfen (etwa mit einem neuen Scan).

</details>

---

<p align="center"><sub>
<a href="installation.md">Installation</a> · <a href="usage.md">Using the app</a> · <a href="command-line.md">Command line</a> · <b>Rules</b> · <a href="how-it-works.md">How it works</a> · <a href="excel-and-pdf.md">Excel &amp; PDF</a> · <a href="data-and-privacy.md">Data &amp; privacy</a> · <a href="limitations.md">Limitations</a> · <a href="development.md">Development</a><br>
<a href="../README.md">← Back to Zutritt</a>
</sub></p>
