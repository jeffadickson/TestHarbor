# Architecture and design decisions

## ADR-001: Wrap at the public fixture boundary

User requirement: a wrapper in front of NUnit, without changing its internals.
`HarborFixture` uses only NUnit.Framework public attributes and TestContext.
It does not implement IWrapSetUpTearDown because that command-level route exposes
NUnit internal command/context types and is unnecessary for this milestone.
The independent TestSession facade also works without any fixture inheritance.

Lifecycle: base SetUp acquires resources and constructs a scope; derived SetUp initializes
services; NUnit runs the normal test; derived TearDown runs; base TearDown awaits scope
and root disposal, then releases resources. OneTimeSetUp and constructors are outside this scope.

## ADR-002: Atomic multi-resource coordination

A single short-held monitor protects ownership counts and a FIFO pending queue.
No user code executes under that monitor. TaskCompletionSource uses asynchronous continuations.
An acquisition grants all resources or none; no partial sets are held by queued requests.
Duplicates collapse to exclusive if any duplicate requests exclusive access.
Cancellation and granting race under the same monitor. If cancellation wins, the queue entry
is removed. If grant wins, the returned lease must be disposed even if its token is now cancelled.
A cancellation token does not revoke ownership from running code.

FIFO prevents younger requests overtaking an older writer; it also causes head-of-line blocking.
Fair queue improvements must preserve starvation bounds. Do not nest acquisitions.

## ADR-003: Dependency ownership

By default each session has its own root provider and async scope, so singleton registrations are per-session. An explicit TestServiceScope can instead own a fixture/run root with isolated child scopes. Microsoft DI owns factory/type-created services and controls their
within-scope disposal order. It does not own externally supplied singleton instances.
No provider is global and no service locator is stored in AsyncLocal.

TestSession disposal is owner-driven and idempotent. Concurrent disposal calls join the same task
and observe the same completion or failure. Scoped cleanup
failure does not prevent attempting root disposal or releasing the resource lease. Disposer
exceptions are observable; not all remaining DI services are guaranteed disposal after an exception.

## ADR-004: Reporting and cancellation

NUnit retains its normal lifecycle error reporting. The independent RunAsync facade aggregates
body and cleanup errors only when both fail. It does not translate NUnit results or assertion types.
Acquisition uses NUnit's current cancellation token in the base fixture. Async cleanup is not
passed that potentially already-cancelled token. Time limits and hard device shutdown are separate concerns.

## Sources checked 2026-10-15

- https://docs.nunit.org/articles/nunit/writing-tests/attributes/setup.html
- https://docs.nunit.org/articles/nunit/writing-tests/attributes/teardown.html
- https://docs.nunit.org/articles/nunit/writing-tests/attributes/fixturelifecycle.html
- https://docs.junit.org/6.1.3/writing-tests/parallel-execution.html
- https://docs.junit.org/6.1.3/extensions/keeping-state-in-extensions.html

JUnit supplies the design inspiration; this implementation is a .NET wrapper with NUnit lifecycle integration.

## ADR-005: Extension rollback and resource quarantine (0.2)

Extensions are ordinary scoped DI services, ordered by registration. Each extension enters the
cleanup stack before StartAsync. StopAsync unwinds every entered extension in reverse order,
without passing the test cancellation token. Services remain available through the extension
context until all stops finish. DI owns subsequent disposal of extension instances and other services.

Cleanup errors establish quarantine before releasing the lease. Quarantine rejects affected
requests anywhere in the FIFO queue immediately, and new requests fail before enqueue. Existing
owners are not revoked. Explicit reset requires a reason and no held leases. Lease operations are
serialized against disposal; attempting quarantine after disposal is an error. No caller callback
executes under the resource coordinator monitor.

The NUnit wrapper checks the public Result.Outcome.Site for derived teardown failure and marks
the session's resources before its own cleanup. Real runner failure probes verify this behavior.
No outcome check relies on NUnit internal types. A test failure alone does not imply broken hardware.

Diagnostics retain 256 events and expose detached read-only status/owner lists. They contain no
service instances. Resource quarantine reasons remain until reset; normal released state is removed.

## Declarative resources (0.2.1)

RequiresResourceAttribute derives from NUnit's public PropertyAttribute. NUnit attaches validated name/mode metadata to the discovered method case or fixture; HarborFixture reads AllPropertyValues for the current case hierarchy during base setup. This avoids ambiguous name reflection and retains inherited metadata for overloaded, generic and parameterized methods. Shared and exclusive declarations use separate reserved string keys.

The resulting requests concatenate with Resources overrides before the existing coordinator normalizes duplicates and acquires the complete set atomically. Attributes do not change lease lifetime, cancellation, quarantine, or scheduler behavior. No hardware is acquired during discovery. Plain NUnit fixtures without HarborFixture only receive metadata.

Public API reference: https://docs.nunit.org/articles/nunit/writing-tests/attributes/property.html and https://docs.nunit.org/articles/nunit/writing-tests/TestContext.html .

## ADR-006: Explicit scope and outcome boundaries (0.3)

Reusable providers are caller-owned. They stop accepting sessions when closure begins, await child scopes, then dispose singleton services. Scope-lifetime hardware leases outlive singleton cleanup. Child cleanup failures fault the shared owner and propagate quarantine; overlapping child requests for parent-owned resources are rejected.

Session observations distinguish explicit body timing from outer NUnit lifecycle snapshots. Observer exceptions remain failures. Owned non-DI resources unwind independently before DI cleanup; temporary directories are finalized after service disposal. Retained artifact stores and bounded lifecycle journals are independent of the coordinator ring.

## ADR-007: Conformance and feasibility (0.3)

The Testing package exposes a real-session probe. External adapter verifiers independently check final results and evidence for failures, conditions, filtering, attachments and lifecycle ownership. Package-consumer tests reference packed packages rather than project references.

The [wrapper boundary decisions](WRAPPER-BOUNDARIES.md) resolve transparent injection and runtime node registration with explicit alternatives. Parameterized sources, contract fixtures and nested contexts retain NUnit discovery and reporting.

## ADR-008: Single-host broker (0.3)

An optional file broker uses exclusive open handles and flushed, atomically replaced state on a trusted local filesystem. Multi-resource acquisition drops partial handles before retrying. Active crash records fail closed; successful grants advance fencing generations. Optional heartbeat deadlines quarantine without releasing active ownership. Operator recovery requires obtaining the exclusive handle first. This is separate from the lightweight in-process reader/writer coordinator and is not a multi-host lease service.
