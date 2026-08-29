namespace WorldTime;

/// <summary>
/// Simple wrapper for <seealso cref="Lazy{T}"/> meant for async use.
/// </summary>
// TODO consider moving to Core if others have a use for it
class AsyncLazy<T>(Func<Task<T>> valueFactory)
{
    private readonly Lazy<Task<T>> _lazyTask = new(valueFactory);

    public Task<T> Task => _lazyTask.Value;
}
