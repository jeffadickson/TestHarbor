# NUnit data and injection compatibility — ten local iterations

TestHarbor now supports ordinary NUnit parameter-based data generation alongside constructor,
method, collection and keyed service injection. NUnit owns the data generation and assertions;
TestHarbor removes service parameters from both of NUnit's parameter views and supplies those
services when the selected case executes. This work builds on the previous five local keyed
injection iterations. Both batches are included in the TH-034 publication. Counts below describe this earlier
milestone; see [current validation](VALIDATION.md).

## Requirements and iteration results

| Iteration | Requirement and executable evidence | Targeted result |
|---|---|---|
| 1 | Values and Combinatorial around an injected service; each case has fresh state | 4 passed |
| 2 | Integer and fractional Range with keyed/unkeyed service positions | 6 passed |
| 3 | Pairwise over three data dimensions; verify every value pair in TRX observations | 4 passed |
| 4 | Sequential columns remain aligned around a keyed service | 3 passed |
| 5 | Reject Values on ordinary/keyed service parameters and constructor dependencies | 3 expected discovery failures |
| 6 | Task-based and async-stream sources; preserve names and filtered replay identity | 4 passed |
| 7 | ValueTask and ValueTask<T>/ExpectedResult finish before teardown | 3 passed |
| 8 | Closed generic base contracts inherit service and data parameters correctly | 2 passed |
| 9 | Automatic enum/nullable Boolean expansion, nullable data, decimal conversion and plain NUnit baseline | 12 passed after fixing conversion |
| 10 | Repeatable verifier, XML reference and isolated-cache package consumer with counted results | Full local acceptance |

The 38 new passing cases and three expected failures are in
`examples/TestHarbor.Example/Injection/InjectionDataCompatibilityTests.cs` and
`DataBindingInvalidProbes.cs`. `tools/verify_injection.py` checks the case counts, complete
pairwise coverage, all six enum/nullable-Boolean combinations, stable filtered async-source
identity and explicit discovery failures. It reads case output once rather than counting the
runner's duplicated aggregate output.

## Example

```csharp
/// <summary>Checks quantity data against the same service supplied to the fixture.</summary>
/// <param name="quantity">Generated ordinary NUnit data.</param>
/// <param name="same">A scoped dependency injected at execution time.</param>
[Test]
public void Quantity([Range(2, 6, 2)] int quantity, [FromServices] KeyedEndpoint same)
{
    Assert.That(quantity, Is.AnyOf(2, 4, 6));
    Assert.That(same.Requests, Is.Empty);
}
```

Use this inside an injected fixture whose profile registers KeyedEndpoint, as in the runnable
example. Values/Range belong on data parameters. A parameter marked FromServices or
FromKeyedServices cannot also use Values/Range/another parameter data source. Constructor
parameters are always services, so data-source attributes there are also rejected.

## Decimal conversion fix

The tests exposed an actual defect: a double literal in TestCase was not converted to decimal
when the original method also contained an injected service. NUnit 5.0.0's
`TestCaseParameters.AdjustArgumentsForMethod` reads `IMethodInfo.MethodInfo.GetParameters`,
whereas other discovery code reads `IMethodInfo.GetParameters`. Previously only the latter
excluded service parameters. The mismatched argument counts skipped NUnit's attribute conversion.

`DataMethodInfo` now delegates the original method metadata while filtering its data signature.
NUnit consequently performs its own argument conversion before invocation. TestHarbor still
invokes the original method with the complete service-plus-data argument array. No separate
conversion rules, private reflection access or new NUnit implementation-type aliases were added.
The original source name, declaring type, module/token, custom attributes and return type remain
available; actual IDE source navigation/debugger behavior remains unverified.

Source inspected: NUnit tag v5.0.0, `Internal/TestCaseParameters.cs` and
`Attributes/TestCaseAttribute.cs`. Existing pinning to NUnit 5.0.0 remains required.

## Scope and limits

Async test-case sources execute during discovery and must not depend on a test's runtime DI
scope. Test bodies may return the verified Task/ValueTask forms. Generic test methods and open
generic fixtures remain unsupported; a concrete fixture can inherit a closed generic contract.
Optional/ref/out/params signatures and one-time fixture hooks retain their existing restrictions.
These examples prove the listed combinations, not every custom data provider or NUnit extension.

The package consumer now exercises five actual NUnit cases, including generated Values and the
decimal-conversion regression. An isolated NuGet cache prevents reuse of an older alpha package;
parsing TRX prevents a zero-test invocation from being mistaken for a successful package check.

Run all acceptance gates with:

```sh
DOTNET=/path/to/dotnet python tools/verify.py
```

The original local batch did not push or publish packages. Repository publication follows in TH-034.

## Final local acceptance result

All ten iterations completed. Full Release verification passed with zero build warnings/errors:
164 normal cases (60 library and 104 examples), all external conformance/failure/discovery probes,
817 XML-documented declarations with zero gaps, generated API reference and four local packages.
The isolated-cache package consumer restored those packages and passed all five expected NUnit
cases. The previous five local keyed-injection iterations remain included and verified.

At this historical checkpoint HEAD was `a4253c3` and changes were uncommitted.
Current publication and verification are recorded in TH-034. Actual IDE test explorer/debugger checks
remain open and have not been replaced by CLI evidence.
