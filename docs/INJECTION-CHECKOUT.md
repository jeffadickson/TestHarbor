# Checkout tests: what injection adds

The runnable [comparison](../examples/TestHarbor.Example/Checkout/CheckoutComparison.cs)
contains the same four business cases in two NUnit fixtures:

| Quantity | Expected result | Total | Stock afterward |
|---|---|---|---|
| 1 | Accepted | 1000 cents | 4 |
| 3 | Accepted with quantity discount | 2700 cents | 2 |
| 0 | Rejected without an order | — | 5 |
| 6 | Rejected: only five available | — | 5 |

Both fixtures call the same assertions. Both run parallel cases, with fresh stock and
an empty order history for each case. Everything is in memory; the example needs no
hardware, resource declarations, database, network or mocking package.

## The problem

`CheckoutService` depends on `CheckoutStore` and `CheckoutPricing`. A test needs both
the service and its actual repository so it can check the observable effects of a purchase.
Accidentally constructing a second repository for assertions would inspect the wrong state.
Sharing one repository across cases would let one case change another case's initial stock.

The plain NUnit fixture solves both correctly: setup constructs the graph explicitly,
and `InstancePerTestCase` isolates fixture fields. This is a good choice for a small graph.
NUnit can also be used with manually configured Microsoft DI; it does not automatically
supply these registered services to fixture constructors and test-method parameters.

## What changes with TestHarbor

`CheckoutProfile.Configure` declares three scoped registrations. Microsoft DI constructs
`CheckoutService` and its transitive dependencies. TestHarbor supplies that service to the
fixture constructor, then supplies `CheckoutStore` to the marked method parameter from
the same scope. The accepted-order assertions observe mutations made through the service,
so a mistaken second repository would fail the example.

The injected fixture needs no setup method or nullable dependency fields. The profile can
be reused across fixtures. Ordinary values remain in `[TestCase]`; the service parameter
sits between data parameters without changing the data's order or discovered case identity.
TestHarbor creates a fresh scope for each case; NUnit still owns scheduling and assertions.

For this tiny graph the profile is extra code. The benefit grows when several fixtures
share registration policy, services gain dependencies, or disposable services need scope
cleanup. This example demonstrates graph construction and isolation; disposal failures,
retry/repeat and asynchronous lifecycle behavior have separate injection conformance probes.
It does not claim that dependency injection improves the application algorithm's speed.

## Run it

```sh
dotnet test examples/TestHarbor.Example -c Release --filter 'FullyQualifiedName~CheckoutTests' --logger 'console;verbosity=detailed'
```

Eight passing cases show the two approaches. Each emits a log explaining who constructed
the graph. `tools/verify_injection.py` also verifies all eight results and a single injected
quantity-discount rerun with stable case identity. The normal verification command includes
these checks. IDE test explorer and debugger behavior still require the manual checks in #27.

## Requirements and completed tasks — TH-029 / issue #30

- [x] Keep the comparison software-only and use identical business assertions.
- [x] Exercise transitive constructor injection and method injection from the same scope.
- [x] Mix injected parameters with ordinary NUnit data and run cases in parallel.
- [x] Verify successful orders and rejection without stock/order side effects.
- [x] Explain benefits and costs in test output and documentation.
- [x] Verify filtered replay, XML documentation and the full package verification pipeline.
