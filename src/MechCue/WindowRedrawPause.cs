using System.Runtime.InteropServices;

namespace MechCue;

// Only pause an already-visible chart. WM_SETREDRAW(TRUE) sets WS_VISIBLE,
// so applying it to an initially hidden form would unexpectedly show it.
internal sealed class WindowRedrawPause : IDisposable
{
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern bool RedrawWindow(IntPtr window, IntPtr rectangle, IntPtr region, uint flags);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);
    readonly Control control;
    readonly IntPtr handle;
    bool disposed;

    internal WindowRedrawPause(Control control)
    {
        this.control = control;
        if (!control.IsDisposed && control.IsHandleCreated && IsWindowVisible(control.Handle))
        {
            handle = control.Handle;
            SendMessage(handle, 0x000B, IntPtr.Zero, IntPtr.Zero); // WM_SETREDRAW(FALSE)
        }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (handle == IntPtr.Zero || control.IsDisposed || !control.IsHandleCreated || control.Handle != handle) return;
        bool visible = control.Visible;
        SendMessage(handle, 0x000B, new IntPtr(1), IntPtr.Zero);
        if (!visible) { ShowWindow(handle, 0); return; }
        // Redraw client, border and every child after layout/geometry completes.
        RedrawWindow(handle, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0004 | 0x0080 | 0x0100 | 0x0400);
    }
}
