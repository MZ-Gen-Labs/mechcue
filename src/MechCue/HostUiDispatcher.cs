using System.Runtime.InteropServices;

namespace MechCue;

// Capture the native host's pumping STA during OnConnection, before external
// COM automation can arrive on a different (possibly temporary) RPC thread.
[ComVisible(false)]
public sealed class HostUiDispatcher : IDisposable
{
    readonly Control control = new();
    readonly int ownerThread = Environment.CurrentManagedThreadId;

    public HostUiDispatcher()
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("The host UI dispatcher must be created on the host STA thread.");
        _ = control.Handle;
    }

    public T Invoke<T>(Func<T> action)
    {
        if (control.IsDisposed) throw new ObjectDisposedException(nameof(HostUiDispatcher));
        return Environment.CurrentManagedThreadId == ownerThread ? action() : (T)control.Invoke(action);
    }

    public void Dispose()
    {
        if (!control.IsDisposed) Invoke(() => { control.Dispose(); return true; });
    }
}
