# A concrete problem TestHarbor solves: one failed cleanup contaminates the next test

Suppose integration test A inserts an order into a shared test database. Its assertion passes.
During teardown, the database reset times out and the inserted order remains. Test B expects an
empty table. What stops B from using that contaminated environment?

**NUnit reports A's cleanup failure. TestHarbor additionally records that the named environment
must not be reused until recovery.** Those are different responsibilities.

## The executable comparison

[CleanupFailureComparison.cs](../examples/TestHarbor.Example/Software/CleanupFailureComparison.cs)
runs two actual NUnit fixtures with the same three ordered steps. The table is a deliberately
small in-memory simulation; the reset timeout is injected deterministically. This tests a
lifecycle policy, not a real database outage.

The plain version owns a `SemaphoreSlim` and calls reset in `[TearDown]`, releasing the semaphore
in `finally`. That is ordinary mutual exclusion: it prevents overlapping tests and avoids leaking
a permit on error. It has no persistent record that the table is dirty.

The TestHarbor version acquires `TestDatabase/Orders` in `[SetUp]` and disposes the session in
`[TearDown]`. Its extension calls the **same reset operation**. Cleanup failure quarantines the
resource before releasing ownership. Both versions run under NUnit.

| Step | Plain NUnit with manual locking | NUnit with TestHarbor |
|---|---|---|
| A writes one row | Body assertion passes | Body assertion passes |
| A's reset times out | NUnit reports teardown failure; lock is released | NUnit reports teardown failure; resource is quarantined and released |
| Reset service becomes available again | The old row is still present | The old row is still present |
| B requests the shared table | Lock is available; B runs and reads **one stale row** | Acquisition fails with a quarantine error; **B's body never runs** |
| Operator recovery | Reset succeeds and empty state is verified | Reset succeeds, empty state is verified, then quarantine is explicitly cleared |
| Recovered read | Zero rows | Zero rows, under a newly granted session |

B deliberately asserts that the table is empty. In the plain version it fails on stale data;
without recognizing the earlier cleanup error, this could be mistaken for an application defect.
In the TestHarbor version its reported error identifies the quarantined resource instead.
**Both suites still report failures.** TestHarbor prevents further use of the uncertain environment;
it does not make a failed test pass or repair the database.

## Run the proof

```sh
dotnet build TestHarbor.sln -c Release
python tools/verify_cleanup_comparison.py
```

The script selects the explicit fixtures and expects six results: four deliberate failures and
two successful recovery steps. It also checks the failure reasons and exact event sequences.
It succeeds only if the plain path actually observed stale data, the TestHarbor path did not run
the blocked body, and both recovery checks succeeded.

The key evidence is:

```text
Plain:     write:1 -> reset-failed -> read:1
TestHarbor: write:1 -> reset-failed -> no next-body read
Recovery:  reset-ok -> recovered-read:0
```

Full TRX results, scenario event files and captured runner output are retained in
`examples/TestHarbor.Example/TestResults/cleanup-comparison-*` and uploaded by CI. The verifier
is also part of `python tools/verify.py`. A normal `dotnet test` excludes these Explicit fixtures.
The scenario uses NUnit dependency attributes with `AllowFailure = true` so the next step
executes even after the deliberately failed previous step. The ordering exists only to construct this failure/recovery experiment reproducibly; normal
application tests should not rely on preceding tests having run.

## What was added, exactly?

A mutex answers “is someone using this resource now?” TestHarbor also tracks “is this resource
eligible for a new owner after the last cleanup?” It associates that decision with a resource
name used by cooperating tests, rejects queued or later acquisition after quarantine, and requires
explicit recovery before granting ownership again.

NUnit offers lifecycle hooks where you can implement this policy yourself. Other valid strategies
include creating a fresh database for every test, rolling back a transaction, or refusing to run
a test whose setup cannot establish a clean state. This comparison does not claim NUnit is broken
or that cleanup failure always causes contamination. It demonstrates a specific missing policy
in a shared-environment suite that has locking and cleanup but no quarantine or pre-test reset.

The default coordinator's quarantine is process-local. All users of the shared environment must
participate in the same coordination policy; a caller that bypasses it can still access the data.
For the complementary isolated-database approach, see the real SQLite cases in
[Software examples](SOFTWARE-EXAMPLES.md#4-real-database-integration-sqlite-orders).
