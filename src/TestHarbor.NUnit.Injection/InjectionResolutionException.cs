using System.Reflection;

namespace TestHarbor.NUnit.Injection;

/// <summary>Identifies the injection boundary that could not resolve a dependency.</summary>
/// <remarks>The inner exception retains the container's dependency-chain details and original stack.
/// This diagnostic does not print resolved service values. Inner exceptions are user/container output.</remarks>
public sealed class InjectionResolutionException : InvalidOperationException
{
    /// <summary>Creates a contextual failure without discarding the underlying activation exception.</summary>
    /// <param name="parameter">Constructor or method parameter being resolved.</param>
    /// <param name="reason">Resolution failure category.</param>
    /// <param name="innerException">Original activation exception, when present.</param>
    internal InjectionResolutionException(ParameterInfo parameter, string reason, Exception? innerException = null)
        : base($"{parameter.Member.DeclaringType?.FullName}.{parameter.Member.Name} parameter '{parameter.Name}' ({parameter.ParameterType.FullName}): {reason}.", innerException)
    {
        ServiceType = parameter.ParameterType;
        ParameterName = parameter.Name ?? "(unnamed)";
        MemberName = parameter.Member.Name;
        DeclaringType = parameter.Member.DeclaringType!;
    }

    /// <summary>Gets the requested service type at the injection boundary.</summary>
    /// <value>The constructor or marked method parameter type.</value>
    public Type ServiceType { get; }

    /// <summary>Gets the unresolved parameter's name.</summary>
    /// <value>The reflected parameter name, or an unnamed placeholder.</value>
    public string ParameterName { get; }

    /// <summary>Gets the member being bound.</summary>
    /// <value>The method name or constructor metadata name.</value>
    public string MemberName { get; }

    /// <summary>Gets the type declaring the affected member.</summary>
    /// <value>The declaring fixture or inherited contract type.</value>
    public Type DeclaringType { get; }
}
