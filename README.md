# Zutritt

**Who can access what on your file shares – and through which group.**

Zutritt is a Windows desktop tool (.NET 10, WPF) that reads SMB share permissions and NTFS ACLs, resolves groups down to individual people, flags risky permissions (e.g. *Everyone* can write, orphaned SIDs, rights granted directly to users, hidden inheritance breaks), compares a run with the previous one and produces an Excel or PDF report to sign off during audits (GMP, ISO 27001, NIS2).

Zutritt only reads – it never changes permissions or files.

> [!NOTE]
> Full documentation with screenshots is in progress. The application UI and the reports are in German.

## Download

Every push to `main` updates the pre-release **[latest](https://github.com/ict-baden/zutritt/releases/tag/latest)**:

- `Zutritt-…-x64.msi` – installer for all users
- `Zutritt.exe` – single file, no installation, no .NET runtime required
- `Zutritt-…-win-x64-framework.zip` – small variant, requires the .NET 10 Desktop Runtime

## Build

```powershell
dotnet build Zutritt.sln
dotnet test Zutritt.sln   # integration tests create temporary shares and local accounts – run as administrator
```

---

Part of the ICT-Baden open-source tools.
