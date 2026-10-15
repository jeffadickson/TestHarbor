using NUnit.Framework;
using TestHarbor.NUnit;

namespace TestHarbor.Tests;

/// <summary>Supplies inherited fixture and virtual-method resource declarations.</summary>
[RequiresResource("Attribute/Base", ResourceAccess.SharedRead)]
public abstract class ResourceAttributeBase : HarborFixture
{
    /// <summary>Defines an inherited method resource that an override must retain.</summary>
    [RequiresResource("Attribute/InheritedMethod")]
    public virtual void InheritedMethod() { }
}

/// <summary>Verifies exact-case metadata, inherited declarations and strongest-mode merging in the real runner.</summary>
[RequiresResource("Attribute/Derived", ResourceAccess.SharedRead)]
[RequiresResource("Attribute/Merged", ResourceAccess.SharedRead)]
[Parallelizable(ParallelScope.All)]
public sealed class ResourceAttributeTests : ResourceAttributeBase
{
    /// <summary>Adds dynamic requirements and strengthens a shared declaration.</summary>
    /// <value>The explicit requirements merged atomically with metadata.</value>
    protected override IEnumerable<ResourceRequest> Resources => [new("Attribute/Merged"), new("Attribute/Explicit")];

    /// <summary>Checks inherited and explicit requirements before the test body.</summary>
    [SetUp]
    public void VerifySetup()
    {
        AssertOwned("Attribute/Base", false);
        AssertOwned("Attribute/Derived", false);
        AssertOwned("Attribute/Merged", true);
        AssertOwned("Attribute/Explicit", true);
    }

    /// <summary>Confirms ownership remains active throughout derived teardown.</summary>
    [TearDown]
    public void VerifyTeardown() => VerifySetup();

    /// <summary>Checks the integer overload's own metadata for every generated case.</summary>
    /// <param name="value">Integer case data.</param>
    [TestCase(1)]
    [TestCase(2)]
    [RequiresResource("Attribute/Integer")]
    public void Overloaded(int value)
    {
        Assert.That(value, Is.Positive);
        AssertOwned("Attribute/Integer", true);
        AssertAbsent("Attribute/String");
    }

    /// <summary>Checks the string overload does not borrow the integer overload's declarations.</summary>
    /// <param name="value">String case data.</param>
    [TestCase("text")]
    [RequiresResource("Attribute/String")]
    public void Overloaded(string value)
    {
        Assert.That(value, Is.Not.Empty);
        AssertOwned("Attribute/String", true);
        AssertAbsent("Attribute/Integer");
    }

    /// <summary>Checks constructed generic methods retain their declared resource.</summary>
    /// <typeparam name="T">NUnit-inferred case type.</typeparam>
    /// <param name="value">Typed case input.</param>
    [TestCase(3)]
    [TestCase("generic")]
    [RequiresResource("Attribute/Generic", ResourceAccess.SharedRead)]
    public void Generic<T>(T value)
    {
        Assert.That(value, Is.Not.Null);
        AssertOwned("Attribute/Generic", false);
    }

    /// <summary>Verifies inherited method metadata combines with an overriding method declaration.</summary>
    [Test]
    [RequiresResource("Attribute/Override")]
    public override void InheritedMethod()
    {
        AssertOwned("Attribute/InheritedMethod", true);
        AssertOwned("Attribute/Override", true);
    }

    /// <summary>Checks that discovery validation rejects malformed declarations.</summary>
    [Test]
    public void InvalidDeclarationsFail()
    {
        Assert.Throws<ArgumentException>(() => new RequiresResourceAttribute(" "));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RequiresResourceAttribute("valid", (ResourceAccess)42));
    }

    /// <summary>Checks the current test owns the requested resource in the required mode.</summary>
    /// <param name="name">Resource name to inspect.</param>
    /// <param name="exclusive">Whether an exclusive owner is expected.</param>
    private static void AssertOwned(string name, bool exclusive)
    {
        var state = ResourceCoordinator.Shared.GetSnapshot().Single(x => x.Name == name);
        Assert.That(state.Exclusive, Is.EqualTo(exclusive), name);
        Assert.That(state.Owners.Any(x => x.StartsWith(TestContext.CurrentContext.Test.ID + ": ", StringComparison.Ordinal)), Is.True, name);
    }

    /// <summary>Checks another overload's resource was not acquired by this test.</summary>
    /// <param name="name">Resource that belongs to a different overload.</param>
    private static void AssertAbsent(string name) => Assert.That(ResourceCoordinator.Shared.GetSnapshot()
        .Where(x => x.Name == name).SelectMany(x => x.Owners)
        .Any(x => x.StartsWith(TestContext.CurrentContext.Test.ID + ": ", StringComparison.Ordinal)), Is.False);
}
