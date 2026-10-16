using System.Reflection;
using NUnit.Framework.Interfaces;
using NUnitTypeWrapper = NUnit.Framework.Internal.TypeWrapper;
using NUnitMethodWrapper = NUnit.Framework.Internal.MethodWrapper;

namespace TestHarbor.NUnit.Injection;

/// <summary>Redirects only runtime fixture construction while retaining NUnit's normal type metadata.</summary>
/// <param name="type">Original fixture type.</param>
/// <param name="profile">Runtime registration profile type.</param>
internal sealed class InjectedType(Type type, Type profile) : NUnitTypeWrapper(type), ITypeInfo
{
    /// <summary>Opens ownership and constructs one attempt without discovery-time activation.</summary>
    /// <param name="args">NUnit fixture arguments; injection fixtures accept none.</param>
    /// <returns>A fresh fixture whose session is registered for final cleanup.</returns>
    object ITypeInfo.Construct(object?[]? args) => InjectionRuntime.Construct(Type, profile);
}

/// <summary>Separates discovery case data from runtime services without replacing NUnit's invocation command.</summary>
/// <param name="type">Reflected fixture type, including inherited tests.</param>
/// <param name="method">Original method, retained for source navigation and async detection.</param>
internal sealed class InjectedMethod(Type type, MethodInfo method) : NUnitMethodWrapper(type, method), IMethodInfo
{
    /// <summary>Exposes only case-data parameters to NUnit discovery and argument validation.</summary>
    /// <returns>Original descriptors for parameters not marked FromServices.</returns>
    IParameterInfo[] IMethodInfo.GetParameters() => GetParameters()
        .Where(p => !p.ParameterInfo.IsDefined(typeof(FromServicesAttribute))).ToArray();

    /// <summary>Supplies service parameters immediately before NUnit invokes the original method.</summary>
    /// <param name="fixture">Attempt-local injected fixture.</param>
    /// <param name="args">Case data already validated and converted by NUnit.</param>
    /// <returns>The original return value, including an awaitable handled by NUnit itself.</returns>
    object? IMethodInfo.Invoke(object? fixture, params object?[]? args)
    {
        var session = InjectionRuntime.Session(fixture!);
        var index = 0;
        var bound = MethodInfo.GetParameters().Select(p => p.IsDefined(typeof(FromServicesAttribute))
            ? InjectionRuntime.Resolve(session, p)
            : args![index++]).ToArray();
        return Invoke(fixture, bound);
    }

    /// <summary>Adds the session cleanup wrapper outside NUnit's setup/body/teardown and fixture disposal.</summary>
    /// <typeparam name="T">Requested attribute or extension interface.</typeparam>
    /// <param name="inherit">Whether inherited user attributes are included.</param>
    /// <returns>The original attributes, with the ownership wrapper where required.</returns>
    T[] IReflectionInfo.GetCustomAttributes<T>(bool inherit)
    {
        var attributes = GetCustomAttributes<T>(inherit);
        return typeof(T) == typeof(IWrapSetUpTearDown)
            ? [.. attributes, (T)(object)new InjectionCleanupWrapper()]
            : attributes;
    }
}
