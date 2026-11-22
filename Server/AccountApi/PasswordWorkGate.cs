// One gate shared by registration, login and password change. No waiting queue.
public sealed class PasswordWorkGate
{
    readonly SemaphoreSlim slots;
    public PasswordWorkGate(int capacity = 2)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        slots = new SemaphoreSlim(capacity, capacity);
    }
    public IDisposable? TryEnter() => slots.Wait(0) ? new Lease(slots) : null;
    sealed class Lease(SemaphoreSlim slots) : IDisposable
    {
        SemaphoreSlim? held = slots;
        public void Dispose() => Interlocked.Exchange(ref held, null)?.Release();
    }
}
