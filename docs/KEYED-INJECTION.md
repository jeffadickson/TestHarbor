# Keyed injection — TH-032

Goal: use Microsoft.Extensions.DependencyInjection's `FromKeyedServices` attribute to
select software services by an explicit key at constructor and test-method boundaries.
Keep ordinary NUnit case data separate from service selection and preserve per-attempt scopes.

## Five local iterations

1. Constructor selection: distinguish two keyed services from the unkeyed default.
2. Method binding: hide keyed parameters from NUnit case data; preserve stable filtered discovery.
3. Collections: select only elements for the requested key, with normal lifetime behavior.
4. Failure handling: reject conflicting declarations, diagnose missing/duplicate/null keyed services,
   and prove cleanup after partial resolution.
5. Package verification: run keyed injection from a clean package consumer, regenerate XML reference,
   and run the full local verification pipeline.

This batch was originally developed without pushing. It is included in the TH-034 publication;
the counts below record this earlier milestone. See [current validation](VALIDATION.md).

## Usage and semantics

Use `[FromKeyedServices("primary")]` from `Microsoft.Extensions.DependencyInjection` on
constructor or test-method parameters. No additional `[FromServices]` is needed. Register
implementations with ordinary `AddKeyedScoped`, `AddKeyedTransient` or `AddKeyedSingleton`.
The profile's provider still belongs to one test attempt; a singleton here is not suite-wide.

The runnable `InjectionKeyedTests.cs` example selects primary, replica and default software
endpoints. Keyed method parameters can appear between ordinary NUnit data parameters;
only the data parameters appear in discovered case names. The selected key is fixed by
attribute metadata, not dynamically supplied by each TestCase row.

| Request | Behavior |
|---|---|
| Singular keyed service | Exactly one non-null registration for the key |
| Keyed `IEnumerable<T>` | Matching implementations in container order; none gives an empty collection |
| Explicit null key | Ordinary unkeyed registrations |
| String or integer key | Standard Microsoft DI key equality |
| Duplicate singular registrations | Contextual injection failure; no last-registration-wins shortcut |
| Missing key | Failure for singular request; no fallback to an unkeyed service |
| Parameterless attribute | Rejected: fixture parameters have no parent service key to inherit |
| Both service markers | Rejected as conflicting intent |
| ServiceKey on fixture/test parameter | Rejected: no parent service key exists |

Scoped element identity is shared across constructor and method resolutions. Transient
instances are recreated per resolution. Partial graphs remain container-owned and are cleaned
if a later factory throws or returns null. Original activation exceptions remain attached to
`InjectionResolutionException`. Application service constructors inside the graph retain normal
Microsoft DI behavior; fixture-boundary restrictions do not change that container's own rules.
No keyed lookup caches or custom DI container were introduced.

## Local acceptance requirements

- [x] Explicit constructor key selection and isolation from default registrations.
- [x] Mixed method data/services and stable single-case filtered replay.
- [x] Ordered scoped collections, empty missing-key collections and transient semantics.
- [x] Explicit null and integer keys.
- [x] Missing, duplicate and null keyed service diagnostics.
- [x] Cleanup of earlier collection elements after later failure or null result.
- [x] Discovery errors for conflicting markers, implicit key inheritance and ServiceKey.
- [x] Clean NuGet consumer exercises keyed constructor, method and collection injection.

## Iteration evidence

1. Constructor selection: one targeted case passed.
2. Method binding: three cumulative targeted cases passed.
3. Collections: six cumulative targeted cases passed.
4. Failure handling: eight passing keyed cases, five expected failures, three invalid declarations,
   stable filtered replay and the existing injection conformance suite passed.
5. Final acceptance uses `DOTNET=/path/to/dotnet python tools/verify.py`: full normal suite,
   external failure probes, XML audit/reference, four packages and the clean package consumer.

IDE explorer/debugger validation remains open. Local verification does not establish IDE support
or replace the three-platform CI run that should follow an eventual push.

## Final local result

All five iterations completed. Full Release verification passed: 126 normal tests (60 library,
66 examples), all external failure/discovery probes, and 758 XML-documented declarations with
zero gaps. Four local packages and the generated API reference were produced.

The clean consumer was additionally rerun with an isolated NuGet cache outside its project
folder. This prevents an older package with the same alpha version from satisfying restore,
and prevents cached package source files from entering the consumer's compilation. That
isolated restore, executable probe and both injected NUnit cases passed.

No commits, pushes, GitHub issues, package publication or remote CI runs were created by these
five iterations. Changes remain in the local working tree for review.
