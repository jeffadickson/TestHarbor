using System.Runtime.CompilerServices;

namespace TestHarbor;

/// <summary>Owns explicitly registered non-DI objects and attempts every cleanup in reverse order.</summary>
/// <remarks>Do not register objects already owned by DI or another owner. Registration is thread-safe;
/// concurrent disposal callers join one result. Async disposal takes precedence over IDisposable.</remarks>
public sealed class OwnedResources : IAsyncDisposable
{
    /// <summary>Tracks explicit ownership across all live cleanup stacks without rooting abandoned objects.</summary>
    private static readonly ConditionalWeakTable<object, OwnedResources> Owners = new();
    /// <summary>Protects atomic cross-stack ownership claims.</summary>
    private static readonly object OwnershipGate = new();
    /// <summary>Protects ownership registration and disposal publication.</summary>
    private readonly object gate = new();
    /// <summary>Records unique owned objects in registration order.</summary>
    private readonly List<object> items = [];
    /// <summary>Stores the shared cleanup completion.</summary>
    private Task? cleanup;
    /// <summary>Detects recursive disposal in the cleanup execution context.</summary>
    private readonly AsyncLocal<bool> cleaning = new();

    /// <summary>Transfers disposal responsibility for a disposable object to this owner.</summary>
    /// <typeparam name="T">Disposable object type.</typeparam>
    /// <param name="item">Object not already owned by DI or another owner.</param>
    /// <returns>The same object for convenient initialization.</returns>
    /// <exception cref="ArgumentException">The object is not disposable or is already registered.</exception>
    /// <exception cref="ObjectDisposedException">Cleanup has started.</exception>
    public T Add<T>(T item) where T : class
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item is not IDisposable and not IAsyncDisposable) throw new ArgumentException("Object must be disposable.", nameof(item));
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(cleanup is not null, this);
            if (items.Any(x => ReferenceEquals(x, item))) throw new ArgumentException("Object is already owned.", nameof(item));
            lock (OwnershipGate)
            {
                if (Owners.TryGetValue(item, out _)) throw new ArgumentException("Object is already registered with an owner.", nameof(item));
                Owners.Add(item, this);
            }
            items.Add(item);
        }
        return item;
    }

    /// <summary>Attempts each registered cleanup once, preserving all failures.</summary>
    /// <returns>The shared cleanup completion.</returns>
    public ValueTask DisposeAsync()
    {
        if (cleaning.Value) throw new InvalidOperationException("Recursive owned-resource disposal is unsupported.");
        lock (gate)
        {
            if (cleanup is null)
            {
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                cleanup = completion.Task;
                _ = CompleteAsync(completion);
            }
            return new(cleanup);
        }
    }

    /// <summary>Runs reverse cleanup and completes the already-published promise.</summary>
    /// <param name="completion">Promise observed by all disposal callers.</param>
    /// <returns>A task completing after all cleanup attempts.</returns>
    private async Task CompleteAsync(TaskCompletionSource completion)
    {
        cleaning.Value = true;
        List<Exception> errors = [];
        try
        {
            for (var i = items.Count - 1; i >= 0; i--)
            {
                try
                {
                    if (items[i] is IAsyncDisposable asynchronous) await asynchronous.DisposeAsync().ConfigureAwait(false);
                    else ((IDisposable)items[i]).Dispose();
                }
                catch (Exception error) { errors.Add(error); }
            }
            if (errors.Count > 0) completion.SetException(new AggregateException("Owned resource cleanup failed.", errors));
            else completion.SetResult();
        }
        finally { cleaning.Value = false; }
    }
}
