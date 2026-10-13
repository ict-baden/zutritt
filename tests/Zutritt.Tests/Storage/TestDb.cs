using Microsoft.Data.Sqlite;
using Zutritt.Core.Storage;
using Zutritt.Tests.TestSupport;

namespace Zutritt.Tests.Storage;

/// <summary>Datenbankdatei in einem temporären Ordner; der Ordner wird samt Datei gelöscht.</summary>
internal sealed class TestDb : IDisposable
{
    private readonly TempFolder _dir = new();

    public string Folder => _dir.Path;
    public string File => System.IO.Path.Combine(_dir.Path, "zutritt.db");

    public ScanStore Open() => new(File);

    /// <summary>Eigene Verbindung an der Klasse vorbei, z. B. um eine Datei gezielt zu verändern.</summary>
    public static SqliteConnection Raw(string path)
    {
        var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        c.Open();
        return c;
    }

    public static long Scalar(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
    }

    public static string Text(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToString(cmd.ExecuteScalar()) ?? "";
    }

    public static void Execute(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _dir.Dispose();
}
