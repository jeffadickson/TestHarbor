using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnitTestSuite = NUnit.Framework.Internal.TestSuite;
using NUnitTestFixture = NUnit.Framework.Internal.TestFixture;
using NUnitCaseBuilder = NUnit.Framework.Internal.Builders.DefaultTestCaseBuilder;

namespace TestHarbor.NUnit.Injection;

/// <summary>Builds ordinary NUnit tests whose fixture and method services are created at execution time.</summary>
/// <param name="profileType">Concrete, parameterless InjectionProfile type.</param>
/// <remarks>Uses NUnit 5.0.0 extension contracts. Each case and retry gets a fresh fixture and scope.</remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class HarborInjectedFixtureAttribute(Type profileType) : NUnitAttribute, IFixtureBuilder
{
    /// <summary>Builds discoverable cases without constructing a profile, fixture or service.</summary>
    /// <param name="typeInfo">The user fixture type supplied by NUnit.</param>
    /// <returns>One NUnit fixture, including actionable discovery errors for unsupported declarations.</returns>
    public IEnumerable<NUnitTestSuite> BuildFrom(ITypeInfo typeInfo)
    {
        var descriptor = new InjectedType(typeInfo.Type, profileType);
        var fixture = new NUnitTestFixture(descriptor);
        fixture.ApplyAttributesToTest(typeInfo.Type);
        fixture.LifeCycle = LifeCycle.InstancePerTestCase;
        var problem = ValidateFixture(typeInfo.Type, profileType);
        var builder = new NUnitCaseBuilder();
        foreach (var method in typeInfo.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
        {
            var adapted = new InjectedMethod(typeInfo.Type, method.MethodInfo);
            if (!builder.CanBuildFrom(adapted, fixture)) continue;
            var test = builder.BuildFrom(adapted, fixture);
            var error = problem ?? ValidateMethod(method.MethodInfo);
            if (error is not null) test.MakeInvalid(error);
            fixture.Add(test);
        }
        if (problem is not null && fixture.RunState != RunState.Explicit) fixture.MakeInvalid(problem);
        return [fixture];
    }

    /// <summary>Checks the supported construction and lifecycle subset without activating user code.</summary>
    /// <param name="type">Fixture type to validate.</param>
    /// <param name="profile">Registration profile type to validate.</param>
    /// <returns>An actionable error, or null when the declaration is supported.</returns>
    private static string? ValidateFixture(Type type, Type profile)
    {
        if (typeof(NUnitAttribute).Assembly.GetName().Version != new Version(5, 0, 0, 0))
            return "TestHarbor injection requires NUnit 5.0.0; this framework version has not been validated.";
        if (type.IsAbstract || type.ContainsGenericParameters || type.GetConstructors().Length != 1)
            return $"{type.FullName}: injection requires a concrete non-generic fixture with exactly one public constructor.";
        if (typeof(HarborFixture).IsAssignableFrom(type))
            return "Injected fixtures must not inherit HarborFixture; injection already owns their session.";
        if (!typeof(InjectionProfile).IsAssignableFrom(profile) || profile.IsAbstract || profile.ContainsGenericParameters || profile.GetConstructor(Type.EmptyTypes) is null
            || typeof(IDisposable).IsAssignableFrom(profile) || typeof(IAsyncDisposable).IsAssignableFrom(profile))
            return "Injection profile must be concrete, parameterless, and must not own disposable state.";
        if (type.GetCustomAttributes(true).OfType<IFixtureBuilder>().Count() != 1)
            return "Do not combine HarborInjectedFixture with TestFixture or other fixture builders.";
        var life = type.GetCustomAttribute<FixtureLifeCycleAttribute>(true)
            ?? type.Assembly.GetCustomAttribute<FixtureLifeCycleAttribute>();
        if (life is not null && life.LifeCycle != LifeCycle.InstancePerTestCase)
            return "Injected fixtures require InstancePerTestCase; SingleInstance is unsupported.";
        if (type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Any(m => m.IsDefined(typeof(OneTimeSetUpAttribute), true) || m.IsDefined(typeof(OneTimeTearDownAttribute), true)))
            return "One-time lifecycle hooks are unsupported on injected fixtures; use attempt-scoped extensions.";
        if (type.GetCustomAttributes(true).OfType<IWrapSetUpTearDown>().Any()
            || type.Assembly.GetCustomAttributes().OfType<IWrapSetUpTearDown>().Any())
            return "Custom fixture/assembly setup-teardown wrappers are unsupported with injection.";
        if (type.GetConstructors()[0].GetParameters().Any(p => p.IsOptional || p.ParameterType.IsByRef || p.IsDefined(typeof(ParamArrayAttribute))))
            return "Injected constructors do not support optional, ref/out or params parameters.";
        if (type.IsDefined(typeof(ApartmentAttribute), true) || type.Assembly.IsDefined(typeof(ApartmentAttribute)))
            return "Apartment-bound injection is unsupported; runtime activation uses a thread-pool bridge.";
        return ValidateServiceMarkers(type.GetConstructors()[0].GetParameters());
    }

    /// <summary>Rejects signatures whose binding or execution semantics are outside the verified subset.</summary>
    /// <param name="method">The original user method.</param>
    /// <returns>An actionable error, or null.</returns>
    private static string? ValidateMethod(MethodInfo method)
    {
        if (method.IsStatic || method.GetParameters().Any(p => p.ParameterType.IsByRef ||
                (InjectionRuntime.IsServiceParameter(p) && (p.IsOptional || p.IsDefined(typeof(ParamArrayAttribute))))))
            return $"{method.Name}: injected tests require instance methods without ref/out parameters or optional/params services.";
        if (method.GetCustomAttributes(true).OfType<IWrapSetUpTearDown>().Any() || method.IsDefined(typeof(ApartmentAttribute), true))
            return $"{method.Name}: custom setup/teardown wrappers and apartment-bound execution are unsupported with injection.";
        return ValidateServiceMarkers(method.GetParameters());
    }
    /// <summary>Rejects conflicting selection markers and key inheritance without a parent service.</summary>
    /// <param name="parameters">Constructor or test method parameters.</param>
    /// <returns>An actionable declaration error, or null.</returns>
    private static string? ValidateServiceMarkers(IEnumerable<ParameterInfo> parameters)
    {
        foreach (var parameter in parameters)
        {
            if ((parameter.Member is ConstructorInfo || InjectionRuntime.IsServiceParameter(parameter)) &&
                parameter.GetCustomAttributes().OfType<IParameterDataSource>().Any())
                return $"{parameter.Member.Name} parameter '{parameter.Name}': service parameters cannot also declare parameter-level case data.";
            if (parameter.IsDefined(typeof(ServiceKeyAttribute)))
                return $"{parameter.Member.Name} parameter '{parameter.Name}': ServiceKey is unsupported on fixture/test parameters; no parent service key exists.";
            var keyed = parameter.GetCustomAttribute<FromKeyedServicesAttribute>();
            if (keyed is null) continue;
            if (parameter.IsDefined(typeof(FromServicesAttribute)))
                return $"{parameter.Member.Name} parameter '{parameter.Name}': choose FromServices or FromKeyedServices, not both.";
            if (keyed.LookupMode == ServiceKeyLookupMode.InheritKey)
                return $"{parameter.Member.Name} parameter '{parameter.Name}': FromKeyedServices requires an explicit key; fixture/test parameters cannot inherit a parent service key.";
        }
        return null;
    }

}
