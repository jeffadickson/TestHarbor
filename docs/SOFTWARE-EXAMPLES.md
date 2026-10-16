# TestHarbor for software tests

TestHarbor is useful whenever test infrastructure has a lifetime: a service provider, a shared
test environment, a temporary directory, or diagnostic files. Hardware is one application of
those policies. These examples need no instruments, server, Docker installation or credentials.

| Example | Application behavior | What TestHarbor adds |
|---|---|---|
| Shopping cart | Add prices and calculate a discount | Reusable service registration and fresh per-test service scopes |
| Shared catalog | Insert and read product records | Named environment ownership and ordered reset callbacks |
| Invoice exporter | Read JSON, validate an amount and write an invoice | Isolated scratch directories, failure retention, redacted retained evidence |
| SQLite orders | Commit orders and roll back a failed batch in a real database | Ordered disposal of transactions/connections and database-file lifetime |

All eleven cases are ordinary NUnit cases. NUnit supplies discovery, parameterization, assertions
and results. The application components contain no TestHarbor calls.

For a runnable demonstration of the failure this prevents, see [stale data versus quarantine](CLEANUP-FAILURE-COMPARISON.md). It uses actual NUnit setup/teardown and verifies the reported outcomes.

## 1. Application services: a shopping cart

Source: [ShoppingCartTests.cs](../examples/TestHarbor.Example/Software/ShoppingCartTests.cs).

Two cases add 10 and 20 to a cart and check totals of 30 or 27, depending on the discount.
Each first checks that its cart is empty. Both cases can run in parallel.

`ShoppingProfile` registers the cart once:

```csharp
services.AddScoped<ShoppingCart>();
```

A fixture selects that registration profile with `[UseExtensionProfile(typeof(ShoppingProfile))]`.
It resolves the cart through `Services.GetRequiredService<ShoppingCart>()`. TestHarbor creates a
separate service scope for each case and owns its disposal. There is no shared external state,
so this fixture deliberately has **no RequiresResource attribute**.

For this tiny cart, `new ShoppingCart()` in a plain NUnit test is simpler. The profile becomes
useful when many fixtures need the same application service graph—repositories, clients,
serializers and request-scoped state—and should not each build and dispose that graph manually.
This example demonstrates the mechanism without claiming the tiny application needs it.

## 2. Shared software environment: a catalog

Source: [SharedCatalogTests.cs](../examples/TestHarbor.Example/Software/SharedCatalogTests.cs).

Imagine two integration tests use the same database. Each starts by deleting old test data.
Without coordination, one test can reset the database while the other is checking its records.
Thread-safe database operations alone do not prevent that sequence of events.

The example models this using a shared in-memory catalog. It is a **database stand-in**, not
SQLite or a database transaction test. A thread-safe dictionary holds records; the problem is
coordinating the complete reset/insert/read/reset sequence.

The fixture declares `[RequiresResource("IntegrationEnvironment/Catalog")]`. Its reusable
profile registers the shared catalog and a `CatalogReset` extension. The resulting sequence is:

1. TestHarbor acquires the named catalog resource.
2. The extension resets the catalog.
3. NUnit runs the case, which asserts the catalog is empty, inserts its product, and reads it.
4. The extension resets the catalog again, still under ownership.
5. TestHarbor releases the resource after cleanup.

Both product cases can be offered to NUnit's parallel scheduler, but their shared catalog
sequences cannot overlap. Other cooperating fixtures declare the same name to participate.
The extension contains your environment-specific reset code; TestHarbor supplies its lifetime
and ownership boundary. Cleanup failure would quarantine the resource; failure injection is
covered by the separate conformance/bench suites, not claimed by these two happy-path cases.

In a real database suite, prefer a separate database/schema or a rollback transaction per test
when that gives adequate isolation. Use a named shared resource when tests must operate on one
shared environment. TestHarbor does not implement database transactions or distributed locks.
The default coordinator covers one process; separate test hosts need explicit broker integration.

## 3. File-processing application: invoice export

Source: [InvoiceExportTests.cs](../examples/TestHarbor.Example/Software/InvoiceExportTests.cs).

`InvoiceExporter` is real application code: it reads a decimal amount from JSON, rejects negative
amounts, and writes an invariant-culture invoice. It knows nothing about NUnit or TestHarbor.
The test infrastructure opens a session with an empty resource set because unique working files
do not need shared-resource locks.

Two successful cases:

- Create a unique session-owned directory and write the input JSON.
- Run the exporter and assert the generated text.
- Copy the output to an `ArtifactStore` outside the scratch directory and attach it with NUnit.
- Verify session cleanup removed the scratch directory while the attached copy remains.

A failure case:

- Write a negative amount and let the exporter throw through `TestSession.RunAsync`.
- Assert that failure **outside** the session so the session can observe it.
- Verify the failed session retained its input directory under the default `OnFailure` policy.
- Publish a diagnostic with a supplied example secret redacted, then attach it and its provenance.
- Explicitly remove the retained scratch directory after collecting the evidence.

This is useful for importers, report generators, compilers and data-conversion jobs: successful
tests do not leave scratch files everywhere, while failed operations retain enough context for
diagnosis. The developer still chooses what evidence to keep and which literal secrets to redact.

Directory retention reflects failures observed by the session, not a later final NUnit outcome.
If an exception is swallowed inside the session, it will not automatically trigger failure
retention. Use `Always` when evidence must survive later runner errors, and manage its eventual
removal explicitly. Artifacts are copied to retained storage before temporary files disappear.

## Run them

```sh
dotnet test examples/TestHarbor.Example -c Release --filter FullyQualifiedName~Software --logger "console;verbosity=detailed"
```

Expect eleven passing cases: two shopping-cart, two shared-catalog, three invoice-export, and four SQLite cases.
The negative invoice is an expected exception asserted by NUnit, not an ignored failure.

Messages labeled `Software/DI`, `Software/shared state`, `Software/files` and
`Software/failure evidence` explain the particular behavior demonstrated. Export evidence is
retained under the runner's working directory in `TestResults/software-evidence` and collected
by CI. The three export cases each attach a text file and JSON provenance sidecar.

Run `python tools/verify.py` for the complete repository checks. All examples use the same
.NET 10 baseline and XML documentation policy as the libraries.

## 4. Real database integration: SQLite orders

Source: [SqliteOrderTests.cs](../examples/TestHarbor.Example/Software/SqliteOrderTests.cs).

These four cases use actual file-backed SQLite databases through `Microsoft.Data.Sqlite`,
not the in-memory catalog stand-in above. The example-only dependency and its native SQLite
runtime packages are pinned in the example project's lockfile. Nothing is added to the
TestHarbor library packages, and no database server or Docker installation is required.

- **Committed orders:** insert a customer in a transaction, commit, close the session, then
  read the value from a new read-only connection. A name containing an apostrophe verifies
  that SQL parameters carry application data correctly.
- **Failed batch:** insert one row and then deliberately violate its primary key. The error
  escapes the session. Disposal of the uncommitted transaction rolls it back; an independent
  connection sees zero rows. The session retains the failed database for inspection.
- **Successful cleanup:** create a database with the default directory-retention policy,
  then check the connection is closed and the working directory no longer exists.

The ownership pattern is deliberately small:

```csharp
var connection = session.Owned.Add(new SqliteConnection(connectionString));
connection.Open();
var transaction = session.Owned.Add(connection.BeginTransaction());
// Execute parameterized statements, then commit only when the operation succeeds.
transaction.Commit();
```

TestHarbor disposes explicitly owned objects in reverse registration order: transaction first,
connection second, followed by temporary-directory cleanup. Register a connection before
opening it so a failed open still has an owner. Do not also register these objects with DI
or another ownership stack. Individual commands use ordinary `using` blocks.

**SQLite supplies commit, constraint checks and rollback.** TestHarbor supplies the ownership
order and workspace-retention policy. A plain NUnit test can implement the same behavior with
`using` blocks and `try/finally`; the wrapper is useful when many integration tests need a
consistent lifecycle for several resources and diagnostic files.

Each case uses a unique database file, so cases may run in parallel without named locks.
Connection pooling is disabled to give these short-lived files explicit handle lifetimes.
The tests use synchronous SQLite commands inside an asynchronous session; they do not claim
asynchronous database I/O. Retained directories are explicitly removed after verification,
so the examples do not leave databases behind. Transactions do not undo an external HTTP
request or another non-database side effect.

```sh
dotnet test examples/TestHarbor.Example -c Release --filter FullyQualifiedName~SqliteOrderTests --logger "console;verbosity=detailed"
```

The broader `FullyQualifiedName~Software` filter now runs eleven cases, including the earlier
seven. Reference: Microsoft's [transaction documentation](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions)
and [connection-string options](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/connection-strings).
