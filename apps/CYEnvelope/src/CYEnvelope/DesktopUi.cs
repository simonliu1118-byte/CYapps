using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace CYEnvelope;

internal static class DesktopUi
{
    public static readonly Brush Text = new SolidColorBrush(Color.FromRgb(31, 41, 55));
    public static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(102, 112, 133));
    public static void Dialog(Window window)
    {
        window.FontFamily = new FontFamily("Microsoft JhengHei UI, Microsoft JhengHei, Segoe UI");
        window.FontSize = 14;
        window.Foreground = Text;
        window.Background = new SolidColorBrush(Color.FromRgb(248, 250, 252));
        window.UseLayoutRounding = true;
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        window.ShowInTaskbar = false;
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            SetWindowLong(handle, -20, GetWindowLong(handle, -20) | 0x00000001); // WS_EX_DLGMODALFRAME
            SendMessage(handle, 0x0080, 0, 0); // WM_SETICON, small
            SendMessage(handle, 0x0080, 1, 0); // WM_SETICON, big
            SetWindowPos(handle, 0, 0, 0, 0, 0, 0x27); // frame changed, preserve position/size/z-order
        };
    }
    public static Button Action(string text, RoutedEventHandler click, bool primary = false, bool cancel = false)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0), IsCancel = cancel };
        if (primary) button.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
        button.Click += click;
        return button;
    }
    public static TextBlock Heading(string text) => new()
    {
        Text = text, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12)
    };
    [DllImport("user32.dll")] private static extern int GetWindowLong(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(nint hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern nint SendMessage(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
}
