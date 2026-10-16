# Development tasks

Repository: https://github.com/jeffadickson/TestHarbor . Local task IDs remain stable; linked GitHub issues track implementation and completion evidence.

- [x] TH-001 — Independent multi-resource coordinator and race tests.
- [x] TH-002 — Per-test scoped services and async cleanup.
- [x] TH-003 — Public NUnit fixture lifecycle integration.
- [x] TH-004 — Runnable simulated instrument example.
- [x] TH-005 — XML/HTML API documentation and locked build.
- [x] TH-006 — Explicit setup/body/teardown/disposal failure verification.
- [x] TH-007 — Attribute ergonomics without internal NUnit access. Handle inheritance,
  overloaded methods, generic tests, and parameterized cases without ambiguous reflection.
- [x] TH-008 — Ordered extension pipeline. Define before/after nesting, rollback on partial
  setup, cleanup failure aggregation and async cancellation rules before implementation.
- [x] TH-009 — Resource quarantine policy. Failed hardware cleanup must support refusing
  later owners until an operator or recovery action clears the fault.
- [x] TH-010 — Fixture and run service scopes. Specify parallel sharing and ownership,
  prove cleanup when only a subset of tests is selected, and keep explicit per-test defaults.
- [x] TH-011 — Diagnostics: lease wait time, owner/test IDs, held duration, cancellation,
  and blocked-resource snapshots with public NUnit progress output. Automated leak detection remains future work.
- [x] TH-012 — Cross-process broker. Fenced leases, heartbeat expiry, crash recovery,
  and tests proving two host processes cannot use the same device concurrently.
- [x] TH-013 — Runtime-generated test feasibility. Evaluate public adapter capabilities;
  preserve wrapper-only constraint. Use TestCaseSource when discovery-time data suffices.
- [x] TH-014 — Linux, Windows and macOS CI passed; see docs/VALIDATION.md for the run and tested commit.
- [x] TH-015 — Add supported NUnit/.NET version matrix, package identity and release policy.

## GitHub tracking

- TH-007: [issue #1](https://github.com/jeffadickson/TestHarbor/issues/1)
- TH-010: [issue #2](https://github.com/jeffadickson/TestHarbor/issues/2)
- TH-012: [issue #3](https://github.com/jeffadickson/TestHarbor/issues/3)
- TH-013: [issue #4](https://github.com/jeffadickson/TestHarbor/issues/4)

## Expanded JUnit gap backlog

The [version 0.3 task index](REQUIREMENTS-v0.3.md) now provides the canonical prioritized backlog for the [full Java comparison](JUNIT-GAP-ANALYSIS.md), preserving existing issues #1–#4 and adding #5–#16.

## Version 0.3 disposition

The remaining JG requirements are implemented or resolved by explicit wrapper-boundary decisions. See [requirements and evidence](REQUIREMENTS-v0.3.md). Cross-process support is single-host and never revokes leases on heartbeat expiry. Runtime node registration and transparent constructor/method injection close with supported alternatives, not hidden NUnit internals.

## Contributor workflow

- [x] TH-016 — One-command local/CI verification, SDK diagnosis and contributor onboarding ([issue #17](https://github.com/jeffadickson/TestHarbor/issues/17)).

- [x] TH-017 — Expanded instrument acceptance suite and explanatory logs ([issue #18](https://github.com/jeffadickson/TestHarbor/issues/18)); see [bench demonstration](BENCH-DEMO.md).

- [x] TH-018 — Runnable plain NUnit/TestHarbor charger comparison and benefit walkthrough ([issue #19](https://github.com/jeffadickson/TestHarbor/issues/19)).

- [x] TH-019 — Software-only service, shared-catalog and file-export examples ([issue #20](https://github.com/jeffadickson/TestHarbor/issues/20)).

- [x] TH-020 — Real SQLite transaction, rollback and file-lifetime examples ([issue #21](https://github.com/jeffadickson/TestHarbor/issues/21)).
