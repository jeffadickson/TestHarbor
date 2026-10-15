using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using TestHarbor.NUnit;

namespace TestHarbor.Tests;

/// <summary>Verifies deadlines, configuration, evidence, typed data and nested ownership boundaries.</summary>
public sealed class AdvancedInfrastructureTests
{
    /// <summary>Checks body deadlines request cancellation and cleanup retains ownership past its warning threshold.</summary>
    /// <returns>A task completing after the cleanup handshake releases the resource.</returns>
    [Test]
    public async Task BudgetsRetainOwnershipDuringOverdueCleanup()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var overdue = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new ResourceCoordinator();
        var run = TestSession.RunAsync([new("deadline")], (s, token) =>
        {
            s.Owned.Add(new AsyncDisposable(() => release.Task));
            return Task.Delay(Timeout.Infinite, token);
        }, coordinator: coordinator, options: new()
        {
            Budgets = new() { Body = TimeSpan.FromMilliseconds(20), CleanupWarningAfter = TimeSpan.FromMilliseconds(20) },
            Observer = o => { if (o.Phase == SessionPhase.CleanupOverdue) overdue.TrySetResult(); }
        });
        try
        {
            await overdue.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(coordinator.GetSnapshot().Single().Exclusive, Is.True);
            Assert.That(run.IsCompleted, Is.False);
        }
        finally { release.TrySetResult(); }
        await Assert.ThrowsAsync<TaskCanceledException>(async () => await run);
        Assert.That(coordinator.GetSnapshot(), Is.Empty);
    }

    /// <summary>Checks shared singleton cleanup holds its scope lease and persists quarantine on failure.</summary>
    /// <returns>A task completing after the failed root cleanup is observed.</returns>
    [Test]
    public async Task SharedCleanupQuarantinesScopeResources()
    {
        var coordinator = new ResourceCoordinator();
        var owner = await TestServiceScope.OpenAsync([new("shared")], services => services.AddSingleton(_ =>
            new AsyncDisposable(() =>
            {
                Assert.That(coordinator.GetSnapshot().Single().Exclusive, Is.True);
                throw new IOException("shared-cleanup");
            })), coordinator);
        await using (var child = await TestSession.OpenAsync([], options: new() { ServiceScope = owner }))
            child.Services.GetRequiredService<AsyncDisposable>();
        await Assert.ThrowsAsync<IOException>(async () => await owner.DisposeAsync());
        Assert.That(coordinator.GetSnapshot().Single().QuarantineReason, Is.Not.Null);
    }

    /// <summary>Checks precedence, type conversion, unknown-key rejection and diagnostic redaction.</summary>
    [Test]
    public void ConfigurationIsTypedAndRedacted()
    {
        var file = Path.GetTempFileName();
        var prefix = "TESTHARBOR_CONFIG_" + Guid.NewGuid().ToString("N") + "_";
        try
        {
            File.WriteAllText(file, "{\"count\":\"2\",\"token\":\"private\"}");
            Environment.SetEnvironmentVariable(prefix + "count", "3");
            var settings = new[] { new ConfigurationSetting("count", "1"), new ConfigurationSetting("token", Secret: true) };
            var config = new TestConfiguration(settings, file, prefix, new Dictionary<string, string?> { ["count"] = "4" });
            Assert.That(config.Get("count", int.Parse), Is.EqualTo(4));
            Assert.That(config.Describe()["token"], Is.EqualTo("[redacted]"));
            Assert.Throws<ArgumentException>(() => new TestConfiguration(settings, overrides: new Dictionary<string, string?> { ["typo"] = "x" }));
            var invalid = new TestConfiguration(settings, overrides: new Dictionary<string, string?> { ["count"] = "private" });
            Assert.That(Assert.Throws<FormatException>(() => invalid.Get("count", int.Parse))!.ToString(), Does.Not.Contain("private"));
        }
        finally { File.Delete(file); Environment.SetEnvironmentVariable(prefix + "count", null); }
    }

    /// <summary>Checks copied artifacts survive source deletion and enforce redaction and size limits.</summary>
    /// <returns>A task completing after evidence validation.</returns>
    [Test]
    public async Task EvidenceIsBoundedAndRetained()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ArtifactStore(root, 32);
            var evidence = await store.PublishTextAsync("secret payload", "log.txt", "test", ["secret"]);
            Assert.That(File.ReadAllText(evidence.Path), Is.EqualTo("[redacted] payload"));
            Assert.That(File.Exists(evidence.Path + ".json"), Is.True);
            Assert.That(evidence.Sha256.Length, Is.EqualTo(64));
            await Assert.ThrowsAsync<IOException>(() => store.PublishTextAsync(new string('x', 33), "big.txt", "test"));
            await Assert.ThrowsAsync<ArgumentException>(() => store.PublishTextAsync("x", "../escape", "test"));
            Assert.That(Directory.GetFiles(root).Length, Is.EqualTo(2));
        }
        finally { Directory.Delete(root, true); }
    }

    /// <summary>Checks child state overrides and reverse child-resource disposal.</summary>
    /// <returns>A task completing after nested cleanup.</returns>
    [Test]
    public async Task NestedContextsIsolateAndUnwind()
    {
        var events = new List<string>();
        var root = new HarborContext("root");
        root.Set("parent");
        var a = root.Child("a");
        var b = root.Child("b");
        a.Set("child");
        Assert.That(a.Get<string>(), Is.EqualTo("child"));
        Assert.That(b.Get<string>(), Is.EqualTo("parent"));
        a.Own(new AsyncDisposable(() => { events.Add("a"); return Task.CompletedTask; }));
        b.Own(new AsyncDisposable(() => { events.Add("b"); return Task.CompletedTask; }));
        await root.DisposeAsync();
        Assert.That(events, Is.EqualTo(new[] { "b", "a" }));
    }

    /// <summary>Checks service injection rejects ambiguity while preserving ordinary captured case data.</summary>
    /// <returns>A task completing after service-bound bodies execute.</returns>
    [Test]
    public async Task TypedInvocationRejectsAmbiguity()
    {
        await TestSession.RunAsync([], (s, token) => ServiceInvocation.InvokeAsync<string>(s, (value, _) =>
        { Assert.That(value, Is.EqualTo("one")); return Task.CompletedTask; }, token), c => c.AddSingleton("one"));
        await TestSession.RunAsync([], (s, _) =>
        {
            Assert.Throws<InvalidOperationException>(() => ServiceInvocation.InvokeAsync<string>(s, (_, _) => Task.CompletedTask));
            return Task.CompletedTask;
        }, c => { c.AddSingleton("one"); c.AddSingleton("two"); });
    }

    /// <summary>Checks quoted CSV fields and deterministic source-derived case identities.</summary>
    [Test]
    public void CsvCasesPreserveQuotedFieldsAndIdentity()
    {
        const string csv = "label,value\n\"hello, world\",2.5\n";
        var cases = HarborCases.Csv(new StringReader(csv), (fields, culture) =>
            fields[0] + ":" + double.Parse(fields[1], culture).ToString(CultureInfo.InvariantCulture), "sample-v1").ToArray();
        Assert.That(cases.Single().Arguments[0], Is.EqualTo("hello, world:2.5"));
        var copy = HarborCases.Csv(new StringReader(csv), (fields, _) => fields[0], "sample-v1").Single();
        Assert.That(cases[0].TestName, Is.EqualTo(copy.TestName));
    }

    /// <summary>Checks fixture misuse is rejected before a hidden lifecycle method could run.</summary>
    [Test]
    public void InvalidLifecycleIsRejected() => Assert.Throws<InvalidOperationException>(() => new InvalidFixture());

    /// <summary>Supplies an intentionally incompatible fixture lifecycle without discoverable test methods.</summary>
    [FixtureLifeCycle(LifeCycle.SingleInstance)]
    private sealed class InvalidFixture : HarborFixture { }

    /// <summary>Adapts a callback to an asynchronously disposable resource.</summary>
    /// <param name="dispose">Cleanup callback.</param>
    private sealed class AsyncDisposable(Func<Task> dispose) : IAsyncDisposable
    {
        /// <summary>Awaits the configured cleanup callback.</summary>
        /// <returns>The cleanup result.</returns>
        public ValueTask DisposeAsync() => new(dispose());
    }
}
