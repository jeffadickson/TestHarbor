using Microsoft.Extensions.DependencyInjection;

namespace TestHarbor.NUnit.Injection;

/// <summary>Defines runtime-only registration, conditions and ownership for injected test attempts.</summary>
/// <remarks>Profiles must have a public parameterless constructor and must not own disposable state.</remarks>
public abstract class InjectionProfile : IExtensionProfile
{
    /// <summary>Registers services for one attempt after resource acquisition.</summary>
    /// <param name="services">Attempt-local service registrations.</param>
    public abstract void Configure(IServiceCollection services);

    /// <summary>Gets additional named resources, merged with RequiresResource metadata.</summary>
    /// <value>The resources required before fixture construction.</value>
    public virtual IEnumerable<ResourceRequest> Resources => [];

    /// <summary>Gets configuration used to evaluate conditions before acquisition.</summary>
    /// <value>The attempt's explicit configuration.</value>
    public virtual TestConfiguration Configuration => new([]);

    /// <summary>Gets pure conditions evaluated before resources or services are acquired.</summary>
    /// <value>The ordered execution conditions.</value>
    public virtual IEnumerable<ExecutionCondition> Conditions => [];

    /// <summary>Gets session budgets and observations; shared service scopes are unsupported.</summary>
    /// <value>Options for the fresh attempt-local session.</value>
    public virtual TestSessionOptions Options => new();
}

/// <summary>Marks a test method parameter for runtime service resolution rather than case-data binding.</summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class FromServicesAttribute : Attribute;
