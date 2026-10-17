# Generic method injection — TH-035

TestHarbor supports generic test methods on concrete injected fixtures. NUnit selects the method's
type arguments from ordinary case data or explicit `TypeArgs`. TestHarbor then resolves services
of those concrete types from the current attempt scope. This remains an optional experimental
integration with NUnit 5.0.0; it does not introduce a runner or modify NUnit binaries.

## Why this is useful

A repository, serializer, validator or cache often supports many application types. A generic test
contract can check the same behavior for each type. In `InjectionGenericMethodTests.ClosedService`,
NUnit runs integer and string cases. The profile registers `GenericRepository<>` once; TestHarbor
supplies `GenericRepository<int>` or `GenericRepository<string>` to the selected case and disposes
it afterward. Resolving the same closed service twice returns the same attempt-local instance.

The test's log explains that division of responsibilities. Plain NUnit already supports generic
case data; TestHarbor adds runtime binding and cleanup of the corresponding typed dependencies.
Without TestHarbor, a test author can implement this by explicitly creating the provider, scope and
typed service in each test or a helper. The wrapper makes that arrangement a reusable profile and
method signature while preserving NUnit assertions, scheduling and results.

`KeyedService` extends the example to separate ordinary and archive repositories. `TwoTypes` checks
identifier and value repositories with independently inferred types. `GenericMethodContractTests`
combines a type selected by a concrete fixture with a second type selected by each inherited method.
All examples are software-only and introduce no resource or instrument-management behavior.

## Requirements

- **GM-001:** Preserve injection when NUnit replaces an open method with a closed method descriptor.
  Both executable and data-only reflection views must retain their intended signatures.
- **GM-002:** Infer types exclusively from ordinary case data. Honor explicit `TypeArgs`; never infer
  application types by resolving services or enumerating registrations during discovery.
- **GM-003:** Resolve closed scalar, keyed and collection services with normal scoped identity and
  strict ambiguity/null diagnostics. CLR constraints and inference failures remain discoverable errors.
- **GM-004:** Preserve supported NUnit data preparation, sources, async results, names, categories,
  inherited methods, retry and repeat semantics after generic closure.
- **GM-005:** Prove cleanup outside the body using independently written disposal records, including
  partial resolution failures and fresh service instances on each retry/repeat attempt.
- **GM-006:** Verify named-case replay IDs, freshly built packages, XML documentation and the complete
  Linux/Windows CI pipeline. Actual IDE navigation and debugger behavior remains separately tracked.

## Twenty completed iterations

| Iteration | Implementation or regression behavior | Acceptance |
| --- | --- | --- |
| 1 | Preserve injection descriptors during generic inference | Integer and string cases, 2 passes |
| 2 | Honor explicit generic types and numeric conversion | Int literal becomes long, 1 pass |
| 3 | Resolve an open registration as a closed scoped service | Shared identity and correct values, 2 passes |
| 4 | Resolve keyed closed services independently | Archive and default repositories differ, 2 passes |
| 5 | Resolve closed service collections | Collection element shares scalar identity, 2 passes |
| 6 | Infer two independent method type arguments | Identifier/value services match data types, 2 passes |
| 7 | Preserve generic constraints | Valid struct case passes; string constraint violation fails before activation |
| 8 | Handle service-only generic methods explicitly | Two explicit types pass; omitted types fail inference before activation |
| 9 | Await generic Task results | Two expected-result cases pass |
| 10 | Await generic ValueTask results with keyed services | Two expected-result cases pass |
| 11 | Prepare generic params arrays through the reflection view | Integer, string and explicitly typed empty arrays, 3 passes |
| 12 | Preserve omitted generic defaults | Value/reference defaults, 2 passes |
| 13 | Honor source-provided type arguments | Source integer becomes long, 1 pass |
| 14 | Retain inherited generic method contracts | Concrete base type plus inferred method type, 2 passes |
| 15 | Retain injection and cleanup across retries | Two cases, four distinct disposed services, empty state per attempt |
| 16 | Retain injection and cleanup across repeats | Two cases, six distinct disposed services, empty state per attempt |
| 17 | Preserve concrete failure diagnostics and partial cleanup | Missing, ambiguous and null services fail; all nine constructed services disposed |
| 18 | Preserve named source cases and filtered identity | Two named cases; category/name replay retains test ID |
| 19 | Verify installation from newly built packages | Eleven clean-consumer cases including three generic cases |
| 20 | Integrate durable verification, requirements and XML reference | Full Release gates, independent disposal checker, Linux/Windows CI |

## Implementation boundary

`InjectedMethod` implements `IMethodInfo.MakeGenericMethod` so closing a method returns another
injection descriptor. Without this override, NUnit's base wrapper returns an ordinary descriptor
that would omit TestHarbor binding and cleanup. `DataMethodInfo` delegates generic closure and
retrieval of the generic definition while preserving the filtered data signature. The former blanket
rejection of generic test methods is removed. No additional NUnit internal types are referenced.

The ambiguity probe deliberately registers both an open repository and two closed integer
repositories. Microsoft DI includes all three when enumerating the closed service type, and
TestHarbor reports three matches instead of silently choosing the last registration. The null-factory
probe uses a separate closed interface so the null result is tested without introducing ambiguity.

The verifier requires 32 normal generic cases, 36 independent disposal records, two discovery
errors, and three runtime failures. It checks fresh instance IDs, retry/repeat indices, concrete
service types, post-body contents, filtered case IDs and exact result counts. Zero-test invocations
cannot satisfy acceptance. Evidence is retained under the example project's `TestResults` directory.

## Limits and validation

Use explicit `TypeArgs` when no ordinary data determines a method type. Open generic fixture classes,
ref/out parameters, optional/params services and one-time lifecycle hooks remain unsupported.
Generic parameters do not change those restrictions. This work does not claim new NUnit versions,
IDE compatibility or package-registry publication. Interactive IDE checks remain in
[issue #27](https://github.com/jeffadickson/TestHarbor/issues/27).

Run the complete acceptance pipeline with `DOTNET=/path/to/dotnet python tools/verify.py`.
CI targets Linux and Windows only. Earlier macOS results describe historical commits.

Final local acceptance passed all Release gates: 216 normal passes and two intentional skips,
all external expected-failure probes, 898 XML-documented declarations with zero audit failures,
1,030 generated API entries, and 11 actual clean-consumer passes. Four packages were built locally;
no package-registry publication is part of this milestone.
