using System.Data;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace TestHarbor.Example.Software;

/// <summary>Exercises actual file-backed SQLite transactions with TestHarbor-owned connections and workspaces.</summary>
/// <remarks>Each case creates its own database, so parallel cases require no named resource locks.</remarks>
[Parallelizable(ParallelScope.All)]
public sealed class SqliteOrderTests
{
    /// <summary>Verifies committed application data survives connection disposal and can be read independently.</summary>
    /// <param name="customer">Customer name, including values requiring SQL parameterization.</param>
    /// <returns>A task completing after independent verification and retained-database removal.</returns>
    [TestCase("Ada")]
    [TestCase("O'Brien")]
    public async Task CommittedOrderSurvivesSession(string customer)
    {
        string? directory = null;
        try
        {
            await TestSession.RunAsync([], (session, _) =>
            {
                directory = session.CreateTemporaryDirectory(retention: DirectoryRetention.Always).Path;
                var connection = OpenOwned(session, Path.Combine(directory, "orders.db"));
                CreateSchema(connection);
                var transaction = session.Owned.Add(connection.BeginTransaction());
                Insert(connection, transaction, 1, customer);
                transaction.Commit();
                return Task.CompletedTask;
            });
            // A new connection verifies the committed file after all session-owned handles were disposed.
            using var reader = OpenReader(Path.Combine(directory!, "orders.db"));
            using var query = reader.CreateCommand();
            query.CommandText = "SELECT customer FROM orders WHERE id = 1";
            Assert.That(query.ExecuteScalar(), Is.EqualTo(customer));
            TestContext.Out.WriteLine("[Software/SQLite commit] SQLite committed the order. TestHarbor disposed the transaction before the connection and retained the database as requested. A fresh connection verified the persisted customer, including a quoted name supplied as a parameter.");
        }
        finally { RemoveRetained(directory); }
    }

    /// <summary>Lets a real duplicate-key error escape the session and verifies disposal rolled back its pending insert.</summary>
    /// <returns>A task completing after rollback verification and retained-database removal.</returns>
    [Test]
    public async Task FailedBatchRollsBackAndRetainsDatabase()
    {
        string? directory = null;
        try
        {
            var error = await Assert.ThrowsAsync<SqliteException>(async () => await TestSession.RunAsync([], (session, _) =>
            {
                directory = session.CreateTemporaryDirectory().Path;
                var connection = OpenOwned(session, Path.Combine(directory, "orders.db"));
                CreateSchema(connection);
                var transaction = session.Owned.Add(connection.BeginTransaction());
                Insert(connection, transaction, 1, "first row");
                Insert(connection, transaction, 1, "duplicate row");
                transaction.Commit();
                return Task.CompletedTask;
            }));
            Assert.That(error!.SqliteErrorCode, Is.EqualTo(19), "Expected a real SQLite constraint violation.");
            Assert.That(Directory.Exists(directory), Is.True, "The session observed a failure and should retain its input state.");
            using var reader = OpenReader(Path.Combine(directory!, "orders.db"));
            using var query = reader.CreateCommand();
            query.CommandText = "SELECT COUNT(*) FROM orders";
            Assert.That((long)query.ExecuteScalar()!, Is.Zero, "The first insert must roll back with the failed batch.");
            TestContext.Out.WriteLine("[Software/SQLite rollback] The second insert failed with a duplicate primary key. SQLite rolled back the uncommitted transaction when TestHarbor disposed it, before closing the connection. A new connection sees zero rows; the failed database remains available for diagnosis.");
        }
        finally { RemoveRetained(directory); }
    }

    /// <summary>Verifies normal cleanup closes the database before removing its session-owned directory.</summary>
    /// <returns>A task completing after database connection and file cleanup assertions.</returns>
    [Test]
    public async Task SuccessfulSessionClosesConnectionAndDeletesDatabase()
    {
        string? directory = null;
        SqliteConnection? connection = null;
        await TestSession.RunAsync([], (session, _) =>
        {
            directory = session.CreateTemporaryDirectory().Path;
            connection = OpenOwned(session, Path.Combine(directory, "orders.db"));
            CreateSchema(connection);
            Assert.That(File.Exists(Path.Combine(directory, "orders.db")), Is.True);
            return Task.CompletedTask;
        });
        Assert.That(connection!.State, Is.EqualTo(ConnectionState.Closed));
        Assert.That(Directory.Exists(directory), Is.False);
        TestContext.Out.WriteLine("[Software/SQLite cleanup] TestHarbor closed the owned SQLite connection before deleting the successful workspace. Pooling is disabled for these short-lived test databases. No global lock serializes independent database files.");
    }

    /// <summary>Registers a connection for cleanup before opening its file.</summary>
    /// <param name="session">Session taking sole ownership of the connection.</param>
    /// <param name="file">Absolute database filename.</param>
    /// <returns>An open, session-owned connection.</returns>
    private static SqliteConnection OpenOwned(TestSession session, string file)
    {
        var connection = session.Owned.Add(new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = file, Pooling = false }.ToString()));
        connection.Open();
        return connection;
    }

    /// <summary>Opens an independent read-only connection to an existing database without creating missing files.</summary>
    /// <param name="file">Existing database filename.</param>
    /// <returns>A connection that the caller must dispose.</returns>
    private static SqliteConnection OpenReader(string file)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = file, Pooling = false, Mode = SqliteOpenMode.ReadOnly }.ToString());
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }

    /// <summary>Creates the schema outside the transaction whose data rollback is tested.</summary>
    /// <param name="connection">Connection used to create the orders table.</param>
    private static void CreateSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE orders (id INTEGER PRIMARY KEY, customer TEXT NOT NULL)";
        command.ExecuteNonQuery();
    }

    /// <summary>Inserts an order with explicitly bound SQL parameters in the selected transaction.</summary>
    /// <param name="connection">Connection associated with the transaction.</param>
    /// <param name="transaction">Transaction owning the pending insert.</param>
    /// <param name="id">Unique order identifier.</param>
    /// <param name="customer">Customer name treated as data rather than SQL syntax.</param>
    private static void Insert(SqliteConnection connection, SqliteTransaction transaction, int id, string customer)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO orders (id, customer) VALUES ($id, $customer)";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$customer", customer);
        command.ExecuteNonQuery();
    }

    /// <summary>Removes retained example data after independent verification has closed its readers.</summary>
    /// <param name="directory">Owned example directory, or null if creation did not complete.</param>
    private static void RemoveRetained(string? directory)
    {
        if (directory is not null && Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
