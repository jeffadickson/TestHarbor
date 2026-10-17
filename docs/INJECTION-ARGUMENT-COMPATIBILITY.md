# NUnit argument and provider compatibility — TH-034

This milestone contains twenty additional iterations after the five keyed-injection and
ten data-compatibility iterations. It publishes those accumulated changes together.
The package remains experimental, pinned to NUnit 5.0.0 and targeting .NET 10.

## Requirements and completed tasks

Each row identifies one behavior, its implementation or regression coverage, and its acceptance
check. The first seventeen iterations were exercised through focused real-adapter runs; all
rows are also covered by the complete Release verification pipeline.

| Iteration | Requirement and completed work | Acceptance evidence |
| --- | --- | --- |
| 1 | Allow an omitted optional ordinary argument after a service | `OptionalDefault` receives 7 |
| 2 | Explicit ordinary data overrides an optional default | `OptionalExplicit` receives 9 |
| 3 | Preserve a nullable optional default after keyed injection | `NullableDefault` receives null |
| 4 | Allow ordinary `params` arrays without packing services | `ParamsData` covers zero, one and three elements |
| 5 | Preserve NUnit's single `object[]` argument packing | `ObjectArray` receives mixed integer/string data |
| 6 | Support static property `ValueSource` data | `ValueSource` runs two quantities |
| 7 | Preserve sources declared on another type | `ExternalSource` receives 6 |
| 8 | Forward source arguments through NUnit | `ParameterizedSource` receives the computed quantity 12 |
| 9 | Preserve an array-valued source argument | `ArraySource` receives one two-element array |
| 10 | Preserve source-declared expected results | `SourceResult` returns the expected decimal order total |
| 11 | Preserve names, categories and filtered identity | `NamedCategory` and external category replay share a test ID |
| 12 | Preserve NUnit culture control alongside decimal conversion | `Culture` runs under fr-FR with exact decimal data |
| 13 | Keep enum-valued case data separate from services | `EnumData` receives Express |
| 14 | Preserve NUnit's implicit trailing cancellation token | `Cancellation` receives the current test token |
| 15 | Resolve the built-in attempt `IServiceProvider` | Constructor and method receive the same provider and scoped endpoint |
| 16 | Preserve parent identity when an application creates a child scope | `ChildScope` resolves a different child endpoint and the original parent endpoint |
| 17 | Let Microsoft DI resolve transitive keyed dependencies | `TransitiveKey` shares the directly injected primary endpoint |
| 18 | Honor ignored method and source metadata before activation | Two ignored cases use a profile that throws if constructed |
| 19 | Retain unsupported-signature and explicit-key guards | Four expected failures: optional service, params service, ref data, missing keyed provider |
| 20 | Ship the same behavior through packaged assemblies and document it | Eight actual clean-consumer passes, full Release gates and XML documentation audit |

## What changed and why it helps

`HarborInjectedFixtureAttribute` previously rejected all optional and `params` parameters.
The data-only method descriptor now allows NUnit to prepare these ordinary arguments, so the
restriction only applies to service parameters. Constructor parameters remain required services;
ref/out data and generic test methods remain unsupported.

Microsoft DI provides `IServiceProvider` intrinsically. It does not appear as an enumerable
service registration. The new regression initially failed with “expected exactly one registration;
found 0.” The runtime now returns the attempt provider for an unkeyed provider request (including
an explicit null key). Non-null keyed requests retain strict keyed resolution.

For example, `InjectionArgumentTests.TransitiveKey` receives a `KeyedOrder` application service
and a primary endpoint. `KeyedOrder` itself uses `[FromKeyedServices("primary")]` in its constructor.
The test verifies that both paths reach the same scoped endpoint, distinct from the default endpoint.
Its log explains the benefit: TestHarbor removes repeated provider setup, fixture construction and
scope cleanup from test bodies while NUnit continues to own execution and assertions.

Plain NUnit can implement the same arrangement with explicit setup and teardown; TestHarbor adds
reusable registration profiles and parameter binding. Most rows above are NUnit features being
preserved, not new capabilities invented by TestHarbor. Prefer typed dependencies for routine tests;
the provider is available when the behavior under test actually requires dynamic resolution.

## Evidence and remaining limits

The normal suite requires 184 passing cases: 60 library tests and 124 examples, plus two intentional
ignore guards. `verify_injection.py` separately requires all 20 new argument/provider cases, stable
category replay, both ignored results and all four expected failures with diagnostic text. It rejects
zero-test runs and checks that service types do not leak into case names. The clean package consumer
uses a separate package cache and requires eight passing TRX results from freshly built packages.

All new C# declarations carry XML documentation, including test fixtures, sources and negative probes.
`tools/verify.py` runs the same build, audit, runner probes and package checks used by Linux, macOS
and Windows CI. CI status is associated with the published commit, not inferred from local success.

IDE explorer navigation, gutter execution and debugger behavior still require the interactive checks
tracked in [issue #27](https://github.com/jeffadickson/TestHarbor/issues/27). CLI and adapter evidence
does not close that task. No broader NUnit version compatibility or package-registry publication is claimed.

Final local acceptance: all Release gates passed with zero build warnings/errors, 855 XML-documented
declarations with zero audit failures, 983 generated API entries, and eight clean-consumer passes.
