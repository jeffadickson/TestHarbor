using NUnit.Framework;

namespace TestHarbor.Tests;

/// <summary>Verifies end-of-run evidence without revoking real ownership.</summary>
public sealed class OwnershipAuditTests
{
    /// <summary>Detects a forgotten session even when it owns no named resource.</summary>
    /// <returns>A task completing after explicit cleanup restores idle state.</returns>
    [Test]
    public async Task EmptyResourceSessionIsVisible()
    {
        var coordinator = new ResourceCoordinator();
        await using (var session = await TestSession.OpenAsync([], coordinator: coordinator,
            options: new() { Owner = "forgotten export session" }))
        {
            Assert.That(coordinator.GetSnapshot(), Is.Empty);
            var error = Assert.Throws<OutstandingOwnershipException>(coordinator.ThrowIfNotIdle);
            Assert.That(error!.Audit.Active.Single().Owner, Is.EqualTo("forgotten export session"));
            Assert.That(error.Message, Does.Contain("no named resources"));
            Assert.That(session.Services, Is.Not.Null);
        }
        Assert.DoesNotThrow(coordinator.ThrowIfNotIdle);
    }

    /// <summary>Retains owner and FIFO waiter evidence while leaving the queue blocked.</summary>
    /// <returns>A task completing after owners release and queued work is joined.</returns>
    [Test]
    public async Task AuditDoesNotReleaseOrCancelAndSnapshotIsDetached()
    {
        var coordinator = new ResourceCoordinator();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var holder = await coordinator.AcquireAsync([new("export")], owner: "publisher");
        var first = coordinator.AcquireAsync([new("export")], timeout.Token, "reader one");
        var second = coordinator.AcquireAsync([new("export")], timeout.Token, "reader two");
        var error = Assert.Throws<OutstandingOwnershipException>(coordinator.ThrowIfNotIdle)!;
        var audit = error.Audit;
        Assert.That(audit.Waiting.Select(x => x.Owner), Is.EqualTo(new[] { "reader one", "reader two" }));
        Assert.That(audit.Active.Single().Elapsed, Is.GreaterThanOrEqualTo(TimeSpan.Zero));
        Assert.That(audit.Waiting.All(x => x.Elapsed >= TimeSpan.Zero), Is.True);
        Assert.That(error.Message, Does.Contain("publisher").And.Contain("export").And.Contain("waiting"));
        Assert.That(first.IsCompleted || second.IsCompleted, Is.False);
        Assert.Throws<NotSupportedException>(() => ((IList<OwnershipEntry>)audit.Active).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<ResourceRequest>)audit.Active[0].Resources).Clear());
        holder.Dispose();
        using (var lease = await first.WaitAsync(timeout.Token))
            Assert.That(second.IsCompleted, Is.False);
        using (var lease = await second.WaitAsync(timeout.Token)) { }
        Assert.DoesNotThrow(coordinator.ThrowIfNotIdle);
        Assert.That(audit.Active.Count, Is.EqualTo(1));
        Assert.That(audit.Waiting.Count, Is.EqualTo(2));
    }

    /// <summary>Separates recovery requirements from leaked ownership and retains captured evidence.</summary>
    /// <returns>A task completing after quarantine and explicit recovery are checked.</returns>
    [Test]
    public async Task ReleasedQuarantineIsIdleButRemainsVisible()
    {
        var coordinator = new ResourceCoordinator();
        using var lease = await coordinator.AcquireAsync([new("catalog")], owner: "migration");
        lease.Quarantine("rollback failed");
        Assert.Throws<OutstandingOwnershipException>(coordinator.ThrowIfNotIdle);
        lease.Dispose();
        var audit = coordinator.GetOwnershipAudit();
        Assert.That(audit.IsIdle, Is.True);
        Assert.That(audit.Quarantined.Single(), Is.EqualTo(new QuarantineEntry("catalog", "rollback failed")));
        Assert.DoesNotThrow(coordinator.ThrowIfNotIdle);
        await Assert.ThrowsAsync<ResourceQuarantinedException>(async () =>
            await coordinator.AcquireAsync([new("catalog")]));
        coordinator.ResetQuarantine("catalog", "restored backup");
        Assert.That(audit.Quarantined.Count, Is.EqualTo(1));
        Assert.That(coordinator.GetOwnershipAudit().Quarantined, Is.Empty);
    }

    /// <summary>Ensures equal diagnostic labels do not collapse distinct leases.</summary>
    /// <returns>A task completing after both empty-resource leases are released.</returns>
    [Test]
    public async Task DuplicateLabelsRemainSeparateOwners()
    {
        var coordinator = new ResourceCoordinator();
        using var first = await coordinator.AcquireAsync([], owner: "same");
        using var second = await coordinator.AcquireAsync([], owner: "same");
        Assert.That(coordinator.GetOwnershipAudit().Active.Count, Is.EqualTo(2));
        first.Dispose();
        Assert.That(coordinator.GetOwnershipAudit().Active.Count, Is.EqualTo(1));
        second.Dispose();
        Assert.DoesNotThrow(coordinator.ThrowIfNotIdle);
    }
}
