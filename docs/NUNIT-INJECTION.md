# Runtime constructor and method injection

Status: implemented experimental package, `TestHarbor.NUnit.Injection`, targeting exactly NUnit
5.0.0 and .NET 10. CLI/adapter evidence is automated; IDE interaction remains unverified.

Start with the [plain NUnit versus injected checkout comparison](INJECTION-CHECKOUT.md)
for a software-only example with business assertions and explanatory logs.

## Write an injected test

Reference the injection package and use an explicit profile. These declarations are ordinary
C#; no source generation, NUnit fork, binary patch or custom test adapter is involved.

```csharp
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using TestHarbor.NUnit.Injection;

/// <summary>Registers one application dependency per test attempt.</summary>
public sealed class CartProfile : InjectionProfile
{
    /// <summary>Registers attempt-local services.</summary>
    /// <param name="services">Session registration collection.</param>
    public override void Configure(IServiceCollection services) => services.AddScoped<Cart>();
}

/// <summary>Holds application state that must not leak between tests.</summary>
public sealed class Cart
{
    /// <summary>Gets the products in this cart.</summary>
    /// <value>The attempt-local product list.</value>
    public List<string> Products { get; } = [];
}

/// <summary>Uses the same scoped dependency in the constructor and test method.</summary>
/// <param name="cart">Dependency supplied after acquisition and extension startup.</param>
[HarborInjectedFixture(typeof(CartProfile))]
public sealed class CartTests(Cart cart)
{
    /// <summary>Combines ordinary case data and an explicitly injected service.</summary>
    /// <param name="product">NUnit case data.</param>
    /// <param name="sameCart">The constructor's scoped dependency.</param>
    [TestCase("book"), TestCase("pencil")]
    public void AddsProduct(string product, [FromServices] Cart sameCart)
    {
        Assert.That(sameCart, Is.SameAs(cart));
        Assert.That(cart.Products, Is.Empty);
        cart.Products.Add(product);
        Assert.That(cart.Products, Has.Count.EqualTo(1));
    }
}
```

The runnable [order example](../examples/TestHarbor.Example/Injection/InjectedOrderTests.cs) adds
parallel cases, shared-read and exclusive resource declarations, extension startup, asynchronous
fixture/service disposal, repeat/retry, nullable source data, and awaited return-value assertions.
[Inherited contracts](../examples/TestHarbor.Example/Injection/InjectionContractTests.cs) reuse a
base test method without losing the concrete fixture's service identity.

## What TestHarbor adds

NUnit supplies discovery, data conversion, method invocation/awaiting, setup/teardown, scheduling,
assertions and results. TestHarbor opens the dependency scope before the constructor, resolves
constructor and marked method parameters, and holds declared resources until cleanup finishes.
Services are not disguised as test data: only ordinary data contributes to discovered case names.
A profile is instantiated at execution time, and registering services occurs after acquisition.
Profile constructors and condition/configuration getters must remain free of resource side effects.

Each retry or repeat attempt gets a new fixture and a new DI scope. Singleton registrations in
this profile are session-local; sharing a provider through `Options.ServiceScope` is rejected.
A constructor and a method asking for the same scoped service receive the same instance. Transient
registrations retain normal DI semantics and may produce different instances.

Constructor parameters are all services. Method parameters are data unless explicitly marked
`[FromServices]` or `[FromKeyedServices(key)]`; service parameters may appear before, between or after data parameters. Exactly
one registration is required for each singular injected dependency, and null resolutions are rejected.
`IEnumerable<T>` parameters instead receive the container's ordered collection of services;
see collection injection below.
Runtime parameter resolution failures throw `InjectionResolutionException` with declaring type, member, parameter
name and requested service type. Missing registrations, ambiguous registrations and null factories
have distinct explanations. A missing transitive dependency, circular graph or throwing factory
retains the original exception as `InnerException`, including its dependency-chain details and stack.
The wrapper does not print service values; user/container inner exceptions can contain user text.
NUnit result-control exceptions (such as Ignore) and cancellation pass through unchanged.
Registration graphs validated during session startup still produce the container's validation
errors before parameter binding; the parameter wrapper applies to runtime resolution.
The resolver enumerates registrations to detect ambiguity; any instances created during that check
remain owned by DI and are disposed during failure cleanup.

## Lifecycle and failures

1. NUnit selects a runnable case. Normal Ignore/Explicit behavior applies.
2. Instantiate its profile and evaluate pure profile conditions.
3. Acquire profile resources plus class/method `RequiresResource` declarations.
4. Create the fresh session, register services, and start extensions.
5. Resolve constructor dependencies and construct the fixture.
6. NUnit executes ordinary setup, method binding/body and teardown.
7. NUnit awaits fixture disposal, if it implements `IAsyncDisposable` or `IDisposable`.
8. TestHarbor stops extensions, disposes scoped/root services, quarantines on cleanup failure,
   and releases resource ownership. Remaining cleanup is attempted after earlier errors.

A failed constructor cleans up already-created services and extensions without calling disposal on
a fixture that never finished construction. NUnit never receives that incomplete object. If both
construction and cleanup fail, both exceptions are retained. A failed method/assertion does not
quarantine when cleanup succeeds. NUnit teardown/fixture disposal failure or session cleanup failure
does quarantine. A cleanup error is appended to the existing NUnit result, not substituted for it.

NUnit owns its exact setup/teardown rules. On the pinned version our failed-setup probe still
receives teardown for the entered setup level; TestHarbor does not recreate that scheduling logic.
A final session observation is after lease release but is still not the final runner result.

Use NUnit `CancelAfter` and its final unmarked `CancellationToken` parameter for body cancellation.
Profile acquisition/startup budgets remain supported; `SessionBudgets.Body` applies only to the
explicit `TestSession.RunAsync` API and does not time an injected NUnit method. Cleanup never
inherits an already-cancelled test token. Cancellation is cooperative and never forcibly terminates
user work or releases ownership while cleanup is active. Join child tasks before returning.

## Deliberately restricted initial surface

- Exactly one public constructor; no constructor case-data binding.
- Instance, non-generic test methods. Ordinary optional data and `params` arrays are supported; ref/out parameters and optional/params services remain unsupported.
- Ordinary Test/TestCase/TestCaseSource and NUnit's own return-type validation. Values, Range,
  Combinatorial, Pairwise and Sequential work on ordinary data parameters. Service parameters
  cannot also declare parameter-level case data.
- InstancePerTestCase only. SingleInstance, one-time lifecycle hooks, combining fixture builders,
  deriving from HarborFixture, and custom setup/teardown command wrappers are rejected.
- Apartment attributes are rejected. NUnit's construction hook is synchronous; the implementation
  bridges async acquisition/startup/cleanup through thread-pool tasks with execution-context flow.
  Constructors therefore run on a worker thread. UI/STA/thread-affine services are not supported.
- Put service/extension registration in InjectionProfile.Configure. Other TestHarbor fixture APIs
  are not automatically imported into this authoring model. Existing packages behave as before.
- Resource coordination remains process-local; this package does not integrate FileResourceBroker.

## Extension boundary and version coupling

Inspected NUnit source tag `v5.0.0`, commit `110e911227972b606241cd5782a7c8a51d2c8f9d`.
`IFixtureBuilder` creates a normal NUnit TestFixture whose ITypeInfo redirects Construct. NUnit's
FixturePerTestCaseCommand calls it once per attempt. An IMethodInfo implementation exposes only
data parameters for discovery and fills service parameters during Invoke. A delegating MethodInfo view
exposes the same data-only signature to NUnit argument preparation, while retaining original
identity and async metadata; invocation uses the original complete method. An IWrapSetUpTearDown
command surrounds normal lifecycle/fixture disposal commands and closes the session in finally.
NUnit's repeat/retry commands surround construction, providing a fresh attempt on each repetition.

The reviewed implementation-type references are confined to three package files:

| File | Allowed NUnit implementation types |
|---|---|
| HarborInjectedFixtureAttribute.cs | TestSuite, TestFixture, Builders.DefaultTestCaseBuilder |
| InjectedDescriptors.cs | TypeWrapper, MethodWrapper |
| InjectionRuntime.cs | Commands.TestCommand, Commands.DelegatingTestCommand, TestExecutionContext, TestResult |

`verify_compatibility.py` checks exact alias references and negative examples; no general namespace
imports are permitted. This is a source boundary guard, not a semantic security sandbox. The package
has an exact NUnit dependency and checks framework assembly identity during discovery. Upgrading
NUnit requires a reviewed compatibility change, not an assumption that this seam remains stable.
The normal core/NUnit integration packages retain their original no-implementation-types boundary.

## Verification and IDE status

```sh
dotnet build TestHarbor.sln -c Release
python tools/verify_injection.py
```

The verifier checks discovery without activation, eight normal cases with ten distinct scopes
because of retry/repeat, one filtered replay with stable case identity, seventeen deliberate
failure/skip/cancellation cases, and three invalid declarations. Independent per-service files
verify exact phase order and ownership through cleanup; final snapshots verify no held resources
or waiters, and quarantine only for the expected cleanup failures. Reports and logs are retained
under TestResults and uploaded by CI. Normal tests exclude the deliberate probes.

All four packages are also consumed from a clean temporary NuGet project; it runs an injected
constructor/method NUnit case without project references.

| Environment | Evidence status |
|---|---|
| dotnet test + NUnit3TestAdapter 6.1.0 | Automated discovery, execution, filtered rerun and TRX checks |
| Visual Studio test explorer/debugger | Not verified; task #27 remains open |
| JetBrains Rider test explorer/debugger | Not verified; task #27 remains open |
| VS Code C# testing tools/debugger | Not verified; task #27 remains open |

For each IDE, record versions and check run-all, single-case selection, rerun, source navigation,
constructor/body breakpoints and async stacks before making a support claim. CLI evidence alone
cannot establish those interactions. No IDE plugin is provided or required by the design.

## Diagnostic requirements — TH-030 / issue #31

- [x] Identify constructor or method injection boundaries for transitive activation errors.
- [x] Preserve the original exception and stack as the inner exception.
- [x] Distinguish missing, multiple and null resolutions without printing service values.
- [x] Preserve NUnit result-control exceptions and cancellation.
- [x] Verify real runner results for nested missing services, cycles, factory failures, null
  factories, factory skips and constructor graph failures, including completed scope cleanup.

The external verifier runs six additional explicit diagnostic probes. Five must fail with
contextual diagnostics and one must remain skipped. These are separate from the seventeen
existing lifecycle probes and do not change the normal passing-case count.

## Collection injection — TH-031 / issue #32

Constructor parameters and `[FromServices]` method parameters may use `IEnumerable<T>`.
With ordinary element registrations, every registered implementation is included in registration
order, and no registrations produce an empty collection. A singular `T` still requires exactly
one registration; choosing a collection is explicit rather than silently choosing one implementation.

The runnable `InjectionCollectionTests` example registers two checkout rules under `ICheckoutRule`.
Its constructor receives both rules, and its test method receives the same scoped elements while
NUnit binds the quantity and expected outcome. Parallel cases get independent rule state.
Transient elements are newly resolved at each injection boundary, as with normal Microsoft DI.

TestHarbor asks Microsoft DI to resolve `IEnumerable<T>`, enumerates it once, rejects null elements,
and supplies a typed array through the declared interface. An explicitly registered collection
therefore follows the container's resolution policy. Custom iterator errors retain parameter
context and their original cause. DI owns the services it creates; materializing a collection does
not adopt ownership of externally created objects. Collection object identity is not promised.
Arrays and `IReadOnlyList<T>` are not automatically synthesized for other parameter types.

- [x] Resolve constructor/method collections, including empty collections.
- [x] Preserve order and scoped/transient lifetime semantics without weakening singular ambiguity checks.
- [x] Reject null elements with contextual diagnostics; retain activation failure causes.
- [x] Prove cleanup of an earlier disposable element when a later element fails or returns null.
- [x] Exercise software-only checkout validation, parallel isolation and normal NUnit data binding.

Five passing collection cases and two additional deliberate failure probes run in the full
verification pipeline. The diagnostic probe total is now eight; the original six are retained.

For explicit selection among implementations, see [keyed injection](KEYED-INJECTION.md).
The five local iterations are implemented and verified without a remote push.

See [ten local compatibility iterations](INJECTION-DATA-COMPATIBILITY.md) for parameter-data,
async-source, ValueTask, inherited-contract and decimal-conversion evidence.

## Ordinary arguments and scope providers — TH-034

NUnit prepares optional defaults, `params` arrays, object-array packing, source rows,
expected results and its implicit trailing cancellation token. TestHarbor supplies only
marked service arguments, then invokes the original method. An unkeyed `IServiceProvider`
constructor or method parameter receives the current attempt's provider. Explicit non-null
provider keys still require a matching registration; they never fall back to the unkeyed provider.

See [twenty iteration requirements and evidence](INJECTION-ARGUMENT-COMPATIBILITY.md)
for source metadata, ignored activation guards, package coverage and software examples.
