namespace TestHarbor;

/// <summary>Provides explicit nested behavioral state and reverse cleanup without changing NUnit's test tree.</summary>
/// <remarks>Create a separate root per test. Child lookup falls back to its parent; inherited mutable values
/// require caller synchronization. C# nested classes do not implicitly share enclosing instances.</remarks>
public sealed class HarborContext : IAsyncDisposable
{
    /// <summary>Protects state and child creation from disposal races.</summary>
    private readonly object gate = new();
    /// <summary>Stores state declared in this context.</summary>
    private readonly Dictionary<Type, object> state = [];
    /// <summary>Provides inherited lookup when a type is absent locally.</summary>
    private readonly HarborContext? parent;
    /// <summary>Owns explicitly registered resources and child contexts.</summary>
    private readonly OwnedResources owned = new();
    /// <summary>Prevents state mutation after disposal begins.</summary>
    private bool disposed;
    /// <summary>Gets the contextual label for diagnostics.</summary>
    /// <value>The explicit caller-selected name.</value>
    public string Name { get; }

    /// <summary>Creates an isolated root or privately linked child context.</summary>
    /// <param name="name">Nonempty diagnostic context name.</param>
    /// <param name="parent">Optional parent used only for state lookup.</param>
    public HarborContext(string name, HarborContext? parent = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        this.parent = parent;
    }

    /// <summary>Creates a child that this context will dispose before earlier resources.</summary>
    /// <param name="name">Child diagnostic name.</param>
    /// <returns>The child context owned by this context.</returns>
    public HarborContext Child(string name)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return owned.Add(new HarborContext(name, this));
        }
    }

    /// <summary>Stores one value of a type in this context without transferring ownership.</summary>
    /// <typeparam name="T">Lookup type.</typeparam>
    /// <param name="value">Value visible to this context and its children.</param>
    public void Set<T>(T value) where T : notnull
    {
        lock (gate) { ObjectDisposedException.ThrowIf(disposed, this); state[typeof(T)] = value; }
    }

    /// <summary>Resolves a typed value from this context or its nearest ancestor.</summary>
    /// <typeparam name="T">Lookup type.</typeparam>
    /// <returns>The nearest matching value.</returns>
    public T Get<T>() where T : notnull
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (state.TryGetValue(typeof(T), out var value)) return (T)value;
        }
        return parent is not null ? parent.Get<T>() : throw new KeyNotFoundException(typeof(T).FullName);
    }

    /// <summary>Transfers ownership of a non-DI disposable resource to this context.</summary>
    /// <typeparam name="T">Resource type.</typeparam>
    /// <param name="resource">Resource not already owned elsewhere.</param>
    /// <returns>The same resource.</returns>
    public T Own<T>(T resource) where T : class
    {
        lock (gate) { ObjectDisposedException.ThrowIf(disposed, this); return owned.Add(resource); }
    }

    /// <summary>Disposes child contexts and owned resources in reverse order, attempting all cleanup.</summary>
    /// <returns>The shared cleanup result.</returns>
    public ValueTask DisposeAsync()
    {
        lock (gate) disposed = true;
        return owned.DisposeAsync();
    }
}
