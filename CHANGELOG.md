# Changelog

## 0.2.0-alpha.1 — 2026-10-15

- Ordered async extensions, partial-start rollback, and reverse cleanup.
- Default resource quarantine after cleanup failures, with explicit recovery.
- Public NUnit teardown failure detection and owner-labelled progress diagnostics.
- Immutable status snapshots and a bounded 256-event history.
- Joined concurrent session disposal and additional concurrency/failure verification.
- GitHub repository integration and push/PR CI across Linux, Windows and macOS.

## 0.1.0-alpha.1 — 2026-10-15

Initial standalone TestHarbor project: in-process resource coordination, per-test DI sessions,
public-hook NUnit wrapper, runnable supply example, concurrency and lifecycle failure checks,
XML/HTML API documentation, locked dependencies, and a three-OS CI workflow definition.

## Documentation and gap-analysis update

- Compared 56 JUnit/NUnit/TestHarbor capabilities and added 16 linked requirements/tasks.
- Documented every authored C# declaration, including private helpers, tests, examples and documentation tooling.
- Added a Roslyn documentation audit and negative self-tests to every CI platform.
- Expanded generated XML/HTML references to all C# projects. Runtime behavior is unchanged.

## 0.2.1-alpha.1

- Added RequiresResource attributes for fixture and method ownership using public NUnit property metadata.
- Combined inherited declarations and dynamic requirements with exclusive-wins duplicate handling.
- Verified overloaded, generic and parameterized cases, inherited overrides and full-lifecycle ownership.
- Updated the runnable simulated supply example, XML reference and JG-001/TH-007 tracking.
