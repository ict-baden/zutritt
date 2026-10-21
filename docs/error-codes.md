# Error codes

Every error message in the window and on the command line ends with a code such as **ZUT-0203** – in every language. When you ask for help, please include the code: it tells support exactly which step failed, even if the message itself is in a language they do not read.

**Contents:** [Codes](#codes) · [Where the codes appear](#where-the-codes-appear)

## Codes

| Code | Meaning | What to check |
|---|---|---|
| ZUT-0001 | Unexpected error | Create a diagnostics package (*Info* › *Diagnosepaket erstellen*) and send it with the steps that led to the error. |
| ZUT-0002 | A file or folder could not be read or written | Path, access rights, free space, a program holding the file open. |
| ZUT-0101 | The command line was called wrongly | `Zutritt.exe --help` shows all commands and options. |
| ZUT-0201 | The database could not be created or opened | Path of `--db`, access rights on the folder, the file is not a database. |
| ZUT-0202 | The database is not writable | Read-only file or folder, permissions. Checked before a command-line scan starts – nothing was scanned. |
| ZUT-0203 | A run could not be saved | Disk space, permissions, another program holding the file. On the command line the reports are written anyway and the exit code is 3; in the window the run stays loaded – create a report before closing. |
| ZUT-0204 | Runs could not be read from the database | The file may be locked by another Zutritt or damaged. |
| ZUT-0205 | A run could not be deleted | As ZUT-0204. |
| ZUT-0206 | The database contains invalid data | The file was changed by another program or is damaged; keep a copy for support. |
| ZUT-0207 | The database comes from a newer Zutritt | Update Zutritt. |
| ZUT-0208 | The file is an SQLite database, but not one of Zutritt | Choose another file; Zutritt leaves this one unchanged. |
| ZUT-0209 | The database is open in WAL mode elsewhere | Close Zutritt on the computer where the file lives, then open it over the share again. |
| ZUT-0301 | A report could not be written | The file is open in Excel or a PDF viewer, permissions, disk space. |
| ZUT-0401 | The scan failed | The message names the reason; the log (*Info* › *Diagnosepaket erstellen*) has details. |
| ZUT-0402 | The comparison with an earlier run failed | The run itself is fine; the reports are written without the comparison. |
| ZUT-0501 | The diagnostics package could not be saved | Target folder and permissions. |
| ZUT-0502 | A setting (language) could not be saved | Permissions on `%LocalAppData%\Zutritt\settings.json`. |
| ZUT-0503 | A link could not be opened | Default browser; only `http` and `https` links are opened. |

Codes are never renumbered; new ones are only added.

## Where the codes appear

- **Window:** in the message bar at the top, e.g. *Der Lauf konnte nicht gespeichert werden. … (Fehler ZUT-0203)*.
- **Command line:** on standard error, e.g. `Fehler ZUT-0202: In die Datenbank kann nicht geschrieben werden. …` or `Error ZUT-0202: Cannot write to the database. …`.

Messages *inside* a run – findings, unreadable folders, accounts that could not be resolved – are results of the scan, not errors of Zutritt, and have no code.

---

<p align="center"><sub>
<a href="installation.md">Installation</a> · <a href="usage.md">Using the app</a> · <a href="command-line.md">Command line</a> · <a href="rules.md">Rules</a> · <a href="how-it-works.md">How it works</a> · <a href="excel-and-pdf.md">Excel &amp; PDF</a> · <a href="data-and-privacy.md">Data &amp; privacy</a> · <a href="limitations.md">Limitations</a> · <a href="development.md">Development</a><br>
<a href="../README.md">← Back to Zutritt</a>
</sub></p>
