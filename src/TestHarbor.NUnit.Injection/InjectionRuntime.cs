using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnitCommand = NUnit.Framework.Internal.Commands.TestCommand;
using NUnitDelegatingCommand = NUnit.Framework.Internal.Commands.DelegatingTestCommand;
using NUnitExecutionContext = NUnit.Framework.Internal.TestExecutionContext;
using NUnitResult = NUnit.Framework.Internal.TestResult;

namespace TestHarbor.NUnit.Injection;

/// <summary>Tracks attempt-local scopes by fixture identity without a global service locator.</summary>
internal static class InjectionRuntime
{
    /// <summary>Associates a constructed fixture with the one session that owns its dependencies.</summary>
    private static readonly ConditionalWeakTable<object, TestSession> Sessions = new();

    /// <summary>Retrieves the session for a constructed injected fixture.</summary>
    /// <param name="fixture">The exact fixture instance for the current attempt.</param>
    /// <returns>The owning session.</returns>
    internal static TestSession Session(object fixture) => Sessions.TryGetValue(fixture, out var session)
        ? session : throw new InvalidOperationException("No active injection session exists for this fixture.");

    /// <summary>Resolves a singular dependency or an ordered service collection with parameter diagnostics.</summary>
    /// <param name="session">Session owning the service graph.</param>
    /// <param name="parameter">Constructor or method dependency.</param>
    /// <returns>The sole non-null service, or a materialized collection of non-null services.</returns>
    internal static object Resolve(TestSession session, ParameterInfo parameter)
    {
        object?[] matches;
        try
        {
            if (parameter.ParameterType.IsGenericType &&
                parameter.ParameterType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                var elementType = parameter.ParameterType.GetGenericArguments()[0];
                var collection = (System.Collections.IEnumerable)session.Services.GetRequiredService(parameter.ParameterType);
                var elements = collection.Cast<object?>().ToArray();
                if (elements.Any(x => x is null))
                    throw new InjectionResolutionException(parameter, "a collection factory returned a null element");
                var result = Array.CreateInstance(elementType, elements.Length);
                for (var i = 0; i < elements.Length; i++) result.SetValue(elements[i], i);
                return result;
            }
            matches = session.Services.GetServices(parameter.ParameterType).ToArray();
        }
        catch (Exception error) when (error is not ResultStateException and not OperationCanceledException and not InjectionResolutionException)
        {
            throw new InjectionResolutionException(parameter, "dependency activation failed; inspect the inner exception for the dependency chain", error);
        }
        if (matches.Length != 1)
            throw new InjectionResolutionException(parameter, $"expected exactly one registration; found {matches.Length}");
        if (matches[0] is null)
            throw new InjectionResolutionException(parameter, "the registered factory returned null");
        return matches[0]!;
    }

    /// <summary>Bridges NUnit's synchronous construction hook to asynchronous session acquisition.</summary>
    /// <param name="type">Fixture type whose constructor dependencies are resolved.</param>
    /// <param name="profileType">Runtime configuration type.</param>
    /// <returns>The new fixture after resource acquisition and extension startup.</returns>
    internal static object Construct(Type type, Type profileType) => Task.Run(async () =>
    {
        var profile = (InjectionProfile)Activator.CreateInstance(profileType)!;
        var configuration = profile.Configuration;
        foreach (var condition in profile.Conditions)
        {
            if (!Enum.IsDefined(condition.Disposition)) throw new ArgumentException("Unknown condition disposition.");
            ArgumentException.ThrowIfNullOrWhiteSpace(condition.Reason);
            if (condition.Predicate(configuration)) continue;
            switch (condition.Disposition)
            {
                case ConditionDisposition.Skip: Assert.Ignore(condition.Reason); break;
                case ConditionDisposition.Inconclusive: Assert.Inconclusive(condition.Reason); break;
                case ConditionDisposition.Fail: Assert.Fail(condition.Reason); break;
            }
        }
        var options = profile.Options;
        if (options.ServiceScope is not null) throw new InvalidOperationException("Injected fixtures require a fresh per-attempt service scope.");
        var test = TestContext.CurrentContext.Test;
        var resources = test.AllPropertyValues("TestHarbor.Resource.Exclusive").Select(x => new ResourceRequest((string)x))
            .Concat(test.AllPropertyValues("TestHarbor.Resource.SharedRead").Select(x => new ResourceRequest((string)x, ResourceAccess.SharedRead)))
            .Concat(profile.Resources);
        var session = await TestSession.OpenAsync(resources, profile.Configure,
            cancellationToken: TestContext.CurrentContext.CancellationToken,
            options: options with { Owner = test.ID + ": " + test.FullName });
        try
        {
            var constructor = type.GetConstructors().Single();
            var arguments = constructor.GetParameters().Select(p => Resolve(session, p)).ToArray();
            object fixture;
            try { fixture = constructor.Invoke(arguments); }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                throw;
            }
            Sessions.Add(fixture, session);
            return fixture;
        }
        catch (Exception primary)
        {
            session.RecordFailure(primary);
            try { await session.DisposeAsync(); }
            catch (Exception cleanup) { throw new AggregateException("Injected construction and cleanup both failed.", primary, cleanup); }
            throw;
        }
    }).GetAwaiter().GetResult();

    /// <summary>Closes a constructed fixture's scope after NUnit has finished all inner lifecycle commands.</summary>
    /// <param name="fixture">The attempt-local fixture whose session must be removed.</param>
    /// <param name="result">NUnit's provisional result after teardown and fixture disposal.</param>
    internal static void Close(object fixture, NUnitResult result)
    {
        if (!Sessions.TryGetValue(fixture, out var session)) return;
        Sessions.Remove(fixture);
        Task.Run(async () =>
        {
            if (result.ResultState.Status == TestStatus.Failed)
                session.RecordFailure(new InvalidOperationException("NUnit provisional failure: " + result.Message));
            if (result.ResultState.Site == FailureSite.TearDown)
                session.QuarantineResources("Injected fixture teardown/disposal failed: " + result.Message);
            await session.DisposeAsync();
        }).GetAwaiter().GetResult();
    }
}

/// <summary>Adds final scope cleanup through NUnit's documented command-wrapper contract.</summary>
internal sealed class InjectionCleanupWrapper : IWrapSetUpTearDown
{
    /// <summary>Wraps NUnit lifecycle commands while preserving their normal result handling.</summary>
    /// <param name="command">NUnit setup/body/teardown and optional fixture disposal.</param>
    /// <returns>A wrapper that always closes the attempt's service scope.</returns>
    public NUnitCommand Wrap(NUnitCommand command) => new InjectionCleanupCommand(command);
}

/// <summary>Ensures a failed assertion or teardown cannot bypass injected service cleanup.</summary>
/// <param name="inner">NUnit's existing command chain.</param>
internal sealed class InjectionCleanupCommand(NUnitCommand inner) : NUnitDelegatingCommand(inner)
{
    /// <summary>Runs NUnit's lifecycle and appends cleanup failures without erasing the primary failure.</summary>
    /// <param name="context">NUnit's current execution context.</param>
    /// <returns>The final result of this attempt, including scope cleanup errors.</returns>
    public override NUnitResult Execute(NUnitExecutionContext context)
    {
        try { context.CurrentResult = innerCommand.Execute(context); }
        catch (Exception error) { context.CurrentResult.RecordException(error); }
        finally
        {
            try { if (context.TestObject is not null) InjectionRuntime.Close(context.TestObject, context.CurrentResult); }
            catch (Exception error) { context.CurrentResult.RecordTearDownException(error); }
        }
        return context.CurrentResult;
    }
}
