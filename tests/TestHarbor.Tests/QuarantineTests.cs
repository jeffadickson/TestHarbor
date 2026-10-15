using NUnit.Framework;

namespace TestHarbor.Tests;

/// <summary>Verifies quarantine rejection, guarded recovery and resource diagnostics.</summary>
public sealed class QuarantineTests
{
    /// <summary>Verifies quarantine rejects all affected waiters, including those behind an unrelated blocked head.</summary>
    /// <returns>A task completing when the verification and all awaited cleanup have finished.</returns>
    [Test]
    public async Task QuarantineRejectsAllAffectedWaitersAndNewRequestsUntilRecovery()
    {
        var c = new ResourceCoordinator();
        using var held = await c.AcquireAsync([new("A"), new("B")]);
        using var unrelated = await c.AcquireAsync([new("C")]);
        var blockedHead = c.AcquireAsync([new("C")]);
        var blockedTail = c.AcquireAsync([new("A")]);
        held.Quarantine("Device failed cleanup.");
        await Assert.ThrowsAsync<ResourceQuarantinedException>(async () => await blockedTail);
        await Assert.ThrowsAsync<ResourceQuarantinedException>(async () => await c.AcquireAsync([new("B")]));
        Assert.Throws<InvalidOperationException>(() => c.ResetQuarantine("A", "Premature recovery"));
        held.Dispose();
        Assert.Throws<ArgumentException>(() => c.ResetQuarantine("A", " "));
        c.ResetQuarantine("A", "Operator recovered A");
        c.ResetQuarantine("B", "Operator recovered B");
        unrelated.Dispose();
        using var nextHead = await blockedHead.WaitAsync(TimeSpan.FromSeconds(5));
        using var recovered = await c.AcquireAsync([new("A"), new("B")]);
    }

    /// <summary>Verifies quarantine preserves existing readers while blocking new readers until recovery.</summary>
    /// <returns>A task completing when the verification and all awaited cleanup have finished.</returns>
    [Test]
    public async Task ExistingSharedOwnersRetainOwnershipButNewReadersAreRejected()
    {
        var c = new ResourceCoordinator();
        using var first = await c.AcquireAsync([new("A", ResourceAccess.SharedRead)]);
        using var second = await c.AcquireAsync([new("A", ResourceAccess.SharedRead)]);
        first.Quarantine("Unreliable response.");
        first.Dispose();
        Assert.That(c.GetSnapshot().Single().Readers, Is.EqualTo(1));
        await Assert.ThrowsAsync<ResourceQuarantinedException>(async () =>
            await c.AcquireAsync([new("A", ResourceAccess.SharedRead)]));
        second.Dispose();
        c.ResetQuarantine("A", "Device recovered");
    }

    /// <summary>Verifies owner labels, waiting counts, immutable snapshots and event order.</summary>
    /// <returns>A task completing when the verification and all awaited cleanup have finished.</returns>
    [Test]
    public async Task SnapshotsExposeOwnersAndWaitingAndRemainDetached()
    {
        var c = new ResourceCoordinator();
        var held = await c.AcquireAsync([new("A")], owner: "owner-1");
        using var token = new CancellationTokenSource();
        var waiting = c.AcquireAsync([new("A")], token.Token, "owner-2");
        var snapshot = c.GetSnapshot().Single();
        Assert.That(snapshot.Owners, Is.EqualTo(new[] { "owner-1" }));
        Assert.That(snapshot.Waiting, Is.EqualTo(1));
        token.Cancel();
        await Assert.ThrowsAsync<TaskCanceledException>(async () => await waiting);
        held.Dispose();
        Assert.That(c.GetSnapshot(), Is.Empty);
        Assert.That(snapshot.Exclusive, Is.True);
        Assert.That(c.GetEvents().Select(x => x.Kind), Is.EqualTo(new[] { "Queued", "Granted", "Queued", "Cancelled", "Released" }));
        Assert.That(c.GetEvents().All(x => x.Elapsed >= TimeSpan.Zero), Is.True);
    }

    /// <summary>Verifies history remains bounded and released leases cannot quarantine resources.</summary>
    /// <returns>A task completing when the verification and all awaited cleanup have finished.</returns>
    [Test]
    public async Task HistoryIsBoundedAndReleasedLeaseCannotQuarantine()
    {
        var c = new ResourceCoordinator();
        for (var i = 0; i < 100; i++)
        { using var lease = await c.AcquireAsync([new("A")]); }
        Assert.That(c.GetEvents().Count, Is.EqualTo(256));
        var held = await c.AcquireAsync([new("A")]); held.Dispose();
        Assert.Throws<ObjectDisposedException>(() => held.Quarantine("Too late"));
    }
}
