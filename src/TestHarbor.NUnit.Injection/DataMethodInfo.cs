using System.Globalization;
using System.Reflection;

namespace TestHarbor.NUnit.Injection;

/// <summary>Exposes the data signature to NUnit without replacing the original executable method.</summary>
/// <remarks>NUnit 5 prepares TestCase conversions through MethodInfo.GetParameters rather than
/// IMethodInfo.GetParameters. Both views must exclude services. Invocation through InjectedMethod
/// still uses the original complete signature; all other reflection metadata delegates unchanged.</remarks>
/// <param name="method">Original reflected method.</param>
internal sealed class DataMethodInfo(MethodInfo method) : MethodInfo
{
    /// <summary>Gets the original member name.</summary>
    /// <value>The declared method name.</value>
    public override string Name => method.Name;
    /// <summary>Gets the original declaring type.</summary>
    /// <value>The type defining the method.</value>
    public override Type? DeclaringType => method.DeclaringType;
    /// <summary>Gets the original reflection context.</summary>
    /// <value>The type from which the method was obtained.</value>
    public override Type? ReflectedType => method.ReflectedType;
    /// <summary>Gets the original module for source lookup.</summary>
    /// <value>The declaring module.</value>
    public override Module Module => method.Module;
    /// <summary>Gets the original metadata identity.</summary>
    /// <value>The method token in its module.</value>
    public override int MetadataToken => method.MetadataToken;
    /// <summary>Gets the original method flags.</summary>
    /// <value>The declared reflection attributes.</value>
    public override MethodAttributes Attributes => method.Attributes;
    /// <summary>Gets the original runtime handle.</summary>
    /// <value>The runtime method identity.</value>
    public override RuntimeMethodHandle MethodHandle => method.MethodHandle;
    /// <summary>Gets the unchanged return type for NUnit awaitable handling.</summary>
    /// <value>The original return type.</value>
    public override Type ReturnType => method.ReturnType;
    /// <summary>Gets the unchanged return parameter metadata.</summary>
    /// <value>The original return parameter.</value>
    public override ParameterInfo ReturnParameter => method.ReturnParameter;
    /// <summary>Gets the original return attribute provider.</summary>
    /// <value>The return type's custom attributes.</value>
    public override ICustomAttributeProvider ReturnTypeCustomAttributes => method.ReturnTypeCustomAttributes;
    /// <summary>Gets whether the original method is generic.</summary>
    /// <value>The original generic method flag.</value>
    public override bool IsGenericMethod => method.IsGenericMethod;
    /// <summary>Gets whether the original method is a generic definition.</summary>
    /// <value>The original generic definition flag.</value>
    public override bool IsGenericMethodDefinition => method.IsGenericMethodDefinition;
    /// <summary>Gets whether the original method has unbound generic parameters.</summary>
    /// <value>The original open-generic flag.</value>
    public override bool ContainsGenericParameters => method.ContainsGenericParameters;

    /// <summary>Returns the parameters NUnit must bind from ordinary data.</summary>
    /// <returns>Original parameter descriptors with injected parameters removed.</returns>
    public override ParameterInfo[] GetParameters() => method.GetParameters().Where(p => !InjectionRuntime.IsServiceParameter(p)).ToArray();
    /// <summary>Returns original implementation flags.</summary>
    /// <returns>The method implementation flags.</returns>
    public override MethodImplAttributes GetMethodImplementationFlags() => method.GetMethodImplementationFlags();
    /// <summary>Returns the original base definition.</summary>
    /// <returns>The overridden method's root definition.</returns>
    public override MethodInfo GetBaseDefinition() => method.GetBaseDefinition();
    /// <summary>Returns original generic arguments.</summary>
    /// <returns>The original generic method arguments.</returns>
    public override Type[] GetGenericArguments() => method.GetGenericArguments();
    /// <summary>Returns original attributes used for async detection and NUnit metadata.</summary>
    /// <param name="inherit">Whether inherited attributes are included.</param>
    /// <returns>The original attributes.</returns>
    public override object[] GetCustomAttributes(bool inherit) => method.GetCustomAttributes(inherit);
    /// <summary>Returns original attributes of the requested type.</summary>
    /// <param name="attributeType">Requested attribute type.</param>
    /// <param name="inherit">Whether inherited attributes are included.</param>
    /// <returns>The matching original attributes.</returns>
    public override object[] GetCustomAttributes(Type attributeType, bool inherit) => method.GetCustomAttributes(attributeType, inherit);
    /// <summary>Checks original attribute presence.</summary>
    /// <param name="attributeType">Requested attribute type.</param>
    /// <param name="inherit">Whether inherited attributes are searched.</param>
    /// <returns>Whether the attribute is present.</returns>
    public override bool IsDefined(Type attributeType, bool inherit) => method.IsDefined(attributeType, inherit);
    /// <summary>Returns original custom attribute metadata without changing method identity.</summary>
    /// <returns>The original custom attribute data.</returns>
    public override IList<CustomAttributeData> GetCustomAttributesData() => method.GetCustomAttributesData();
    /// <summary>Delegates complete-argument invocation to the original method.</summary>
    /// <param name="obj">Target fixture.</param>
    /// <param name="invokeAttr">Invocation flags.</param>
    /// <param name="binder">Reflection binder.</param>
    /// <param name="parameters">Complete arguments; injection is supplied by InjectedMethod.</param>
    /// <param name="culture">Binding culture.</param>
    /// <returns>The original invocation result.</returns>
    public override object? Invoke(object? obj, BindingFlags invokeAttr, Binder? binder, object?[]? parameters, CultureInfo? culture) =>
        method.Invoke(obj, invokeAttr, binder, parameters, culture);
}
