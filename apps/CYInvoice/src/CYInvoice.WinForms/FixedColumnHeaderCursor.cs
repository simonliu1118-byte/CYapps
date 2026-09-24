using System.Runtime.InteropServices;

namespace CYInvoice.WinForms;

internal sealed class FixedColumnHeaderCursor : NativeWindow, IDisposable
{
    private const int LvmGetHeader = 0x1000 + 31;
    private const int WmSetCursor = 0x0020;
    private readonly ListView list;

    public FixedColumnHeaderCursor(ListView list)
    {
        this.list = list;
        list.HandleCreated += Attach;
        list.HandleDestroyed += Detach;
        if (list.IsHandleCreated) Attach(list, EventArgs.Empty);
    }

    private void Attach(object? sender, EventArgs eventArgs)
    {
        if (Handle != IntPtr.Zero) ReleaseHandle();
        var header = SendMessage(list.Handle, LvmGetHeader, IntPtr.Zero, IntPtr.Zero);
        if (header != IntPtr.Zero) AssignHandle(header);
    }

    private void Detach(object? sender, EventArgs eventArgs)
    {
        if (Handle != IntPtr.Zero) ReleaseHandle();
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmSetCursor)
        {
            Cursor.Current = Cursors.Default;
            message.Result = (IntPtr)1;
            return;
        }
        base.WndProc(ref message);
    }

    public void Dispose()
    {
        list.HandleCreated -= Attach;
        list.HandleDestroyed -= Detach;
        if (Handle != IntPtr.Zero) ReleaseHandle();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}
