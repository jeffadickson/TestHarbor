# TestHarbor for software tests

TestHarbor is useful whenever test infrastructure has a lifetime: a service provider, a shared
test environment, a temporary directory, or diagnostic files. Hardware is one application of
those policies. These examples need no instruments, server, Docker installation or credentials.

| Example | Application behavior | What TestHarbor adds |
|---|---|---|
| Shopping cart | Add prices and calculate a discount | Reusable service registration and fresh per-test service scopes |
| Shared catalog | Insert and read product records | Named environment ownership and ordered reset callbacks |
| Invoice exporter | Read JSON, validate an amount and write an invoice | Isolated scratch directories, failure retention, redacted retained evidence |

All seven cases are ordinary NUnit cases. NUnit supplies discovery, parameterization, assertions
and results. The application components contain no TestHarbor calls.

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

Expect seven passing cases: two shopping-cart, two shared-catalog, and three invoice-export cases.
The negative invoice is an expected exception asserted by NUnit, not an ignored failure.

Messages labeled `Software/DI`, `Software/shared state`, `Software/files` and
`Software/failure evidence` explain the particular behavior demonstrated. Export evidence is
retained under the runner's working directory in `TestResults/software-evidence` and collected
by CI. The three export cases each attach a text file and JSON provenance sidecar.

Run `python tools/verify.py` for the complete repository checks. All examples use the same
.NET 10 baseline and XML documentation policy as the libraries.
