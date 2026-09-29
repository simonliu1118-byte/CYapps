using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CYEnvelope;

internal static class Program
{
    private static string Output = "";
    private static readonly List<string> Results = [];
    [STAThread]
    private static int Main(string[] args)
    {
        Output = Path.GetFullPath(args[0]); Directory.CreateDirectory(Output);
        var app = new Application();
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
            { Source = new Uri("pack://application:,,,/CYEnvelope;component/Theme.xaml") });
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            var repository = new Repository(Path.Combine(AppContext.BaseDirectory, "Data", "CYEnvelope.db"));
            var contact = new Contact { Name = "範例收件人" };
            contact.Addresses.Add(new() { Label = "公司", Value = "高雄市新興區範例路一號", PostalCode = "800" });
            contact.Phones.Add(new() { Number = "0912-345-678", Note = "範例資料" });
            contact.LastDeliveryIds.Add("delivery-1"); repository.SaveContact(contact);
            var main = new MainWindow(); main.Show(); Flush();
            Capture(main, "01-main-empty");
            var previewLayer = (Canvas)main.FindName("DirectLayer");
            var rows = previewLayer.Children.OfType<Button>().Where(b => b.Tag is string id && id.StartsWith("delivery-")).ToList();
            Assert(rows.Count == 7 && previewLayer.Visibility == Visibility.Visible, "Mail types are clickable on the envelope preview");
            var chosen = (HashSet<string>)Field(main, "_delivery");
            rows[2].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush();
            Assert(chosen.SetEquals(["delivery-3"]), "Clicking a preview row ticks that mail type");
            Capture(main, "01b-main-ticked");
            rows[2].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush();
            Assert(chosen.Count == 0, "Clicking the row again clears it");
            Assert(((ScrollViewer)main.FindName("EntryScroll")).ScrollableHeight < 1, "Default main: all entry fields fit");
            var print = (Button)main.FindName("PrintButton");
            var size = print.RenderSize; print.Focus(); Flush();
            Assert(size == print.RenderSize, "Primary focus preserves geometry");
            Invoke(main, "ChooseContact", contact); Flush(); Capture(main, "02-main-filled");
            main.Width = main.MinWidth; main.Height = main.MinHeight; Flush(); Capture(main, "03-main-minimum");
            Assert(((ScrollViewer)main.FindName("EntryScroll")).ScrollableHeight < 1, "Minimum main: all entry fields fit");
            var icon = main.Icon as BitmapFrame;
            Assert(icon?.Decoder?.Frames.Count == 7, "Window Icon retains all seven ICO frames");
            SaveWindowIcons(main, "main");
            main.Width = 1120; main.Height = 800;
            var contacts = new ContactWindow(repository) { Owner = main }; contacts.Show(); Flush();
            ((ListBox)Field(contacts, "_contacts")).SelectedIndex = 0; Flush(); Capture(contacts, "04-contacts");
            AssertNoDialogIcon(contacts); contacts.Close();
            var emptyContacts = new ContactWindow(new Repository(Path.Combine(AppContext.BaseDirectory, "EmptyData", "CYEnvelope.db"))) { Owner = main };
            emptyContacts.Show(); Flush();
            Assert(((DataGrid)Field(emptyContacts, "_addresses")).ItemsSource is not null, "Empty contacts: address editor initialized");
            Capture(emptyContacts, "10-contacts-empty"); emptyContacts.Close();
            var format = new FormatWindow(repository, repository.Formats()[0]) { Owner = main };
            format.Show(); Flush(); Capture(format, "05-format"); AssertNoDialogIcon(format); format.Close();
            var settings = repository.Settings();
            var frame = new FrameWindow(settings) { Owner = main }; frame.Show(); Flush(); Capture(frame, "06-frame"); AssertNoDialogIcon(frame); frame.Close();
            var options = new SettingsWindow(settings) { Owner = main }; options.Show(); Flush(); Capture(options, "07-settings"); AssertNoDialogIcon(options); options.Close();
            ((AppSettings)Field(main, "_settings")).DirectEntry = true; Invoke(main, "ApplyEntryMode"); Flush(); Capture(main, "08-direct");
            var layer = (Canvas)main.FindName("DirectLayer");
            var target = layer.Children.OfType<Button>().First(b => (string)b.Tag == "收件人");
            Assert(target.Focusable, "Direct entry fields support keyboard focus");
            target.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush();
            Assert(layer.Children.OfType<TextBox>().Any(), "Direct entry activation creates editor");
            var editor = layer.Children.OfType<TextBox>().Single();
            var scale = editor.TransformToAncestor(main).TransformBounds(new System.Windows.Rect(0, 0, 1, 1)).Width;
            Assert(editor.FontSize * scale >= 13.9 && editor.ActualHeight * scale >= 33.9, "Direct editor remains readable at fitted preview scale");
            Capture(main, "09-direct-edit");
            main.Close();
            // Actual final published EXEs: validate native RT_GROUP_ICON and payload against canonical ICO.
            if (args.Length > 1)
            {
                ValidateIconResources(Path.Combine(args[1], "CYEnvelope.exe"), args[2], "launcher");
                ValidateIconResources(Path.Combine(args[1], "Runtime", "CYEnvelope.exe"), args[2], "runtime");
            }
            File.WriteAllLines(Path.Combine(Output, "checks.txt"), Results);
            app.Shutdown(); return Results.Any(r => r.StartsWith("FAIL ", StringComparison.Ordinal)) ? 1 : 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); File.WriteAllText(Path.Combine(Output, "failure.txt"), ex.ToString()); app.Shutdown(); return 1; }
    }
    private static object Field(object target, string name) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static void Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static void Flush() => Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Assert(bool condition, string text) { var result = (condition ? "PASS " : "FAIL ") + text; Results.Add(result); Console.WriteLine(result); }
    private static void Capture(Window window, string name)
    {
        window.UpdateLayout(); Flush();
        var content = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content); Save(bitmap, name + ".png");
        // Also preserve native non-client chrome from the shown Windows window.
        var hwnd = new WindowInteropHelper(window).Handle;
        GetWindowRect(hwnd, out var r); var dc = GetDC(hwnd); var memory = CreateCompatibleDC(dc);
        var image = CreateCompatibleBitmap(dc, r.Right - r.Left, r.Bottom - r.Top); var old = SelectObject(memory, image);
        try
        {
            if (PrintWindow(hwnd, memory, 2)) Save(Imaging.CreateBitmapSourceFromHBitmap(image, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()), name + "-window.png");
        }
        finally { SelectObject(memory, old); DeleteObject(image); DeleteDC(memory); ReleaseDC(hwnd, dc); }
    }
    private static void Save(BitmapSource bitmap, string name)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(Output, name)); encoder.Save(stream);
    }
    private static void SaveWindowIcons(Window window, string name)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        foreach (var (kind, size) in new[] { (0, 16), (1, 32) })
        {
            var handle = SendMessage(hwnd, 0x007F, kind, 0);
            Assert(handle != 0, $"{name}: native icon {size} exists");
            if (handle == 0) continue;
            var bitmap = Imaging.CreateBitmapSourceFromHIcon(handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            Assert(bitmap.PixelWidth >= size, $"{name}: native icon >= {size}px"); Save(bitmap, $"{name}-icon-{size}.png");
        }
    }
    private static void AssertNoDialogIcon(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        Assert(SendMessage(hwnd, 0x007F, 0, 0) == 0 && SendMessage(hwnd, 0x007F, 1, 0) == 0, window.Title + ": no application title icon");
    }
    private static void ValidateIconResources(string exe, string source, string label)
    {
        var ico = File.ReadAllBytes(source); var module = LoadLibraryEx(exe, 0, 2 | 0x20);
        Assert(module != 0, label + ": load final PE resource image");
        if (module == 0) return;
        try
        {
            byte[]? group = null;
            EnumResourceNames(module, (nint)14, (m, t, n, _) => { group = Resource(m, t, n); return false; }, 0);
            Assert(group is not null, label + ": RT_GROUP_ICON present");
            if (group is null) return;
            var count = BitConverter.ToUInt16(group!, 4); Assert(count == 7, label + ": seven icon sizes");
            for (var i = 0; i < count; i++)
            {
                var offset = 6 + i * 14; var width = group![offset] == 0 ? 256 : group[offset];
                var entry = Enumerable.Range(0, 7).Select(j => 6 + j * 16).Single(j => (ico[j] == 0 ? 256 : ico[j]) == width);
                var expected = ico.AsSpan((int)BitConverter.ToUInt32(ico, entry + 12), (int)BitConverter.ToUInt32(ico, entry + 8));
                var payload = Resource(module, (nint)3, (nint)BitConverter.ToUInt16(group, offset + 12));
                Assert(expected.SequenceEqual(payload), $"{label}: {width}px payload matches canonical ICO");
            }
        }
        finally { FreeLibrary(module); }
    }
    private static byte[] Resource(nint module, nint type, nint name)
    {
        var info = FindResource(module, name, type); var size = SizeofResource(module, info);
        var data = new byte[size]; Marshal.Copy(LockResource(LoadResource(module, info)), data, 0, data.Length); return data;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    private delegate bool EnumName(nint module, nint type, nint name, nint param);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint LoadLibraryEx(string file, nint reserved, uint flags);
    [DllImport("kernel32.dll")] private static extern bool FreeLibrary(nint module);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumResourceNames(nint module, nint type, EnumName callback, nint param);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint FindResource(nint module, nint name, nint type);
    [DllImport("kernel32.dll")] private static extern uint SizeofResource(nint module, nint resource);
    [DllImport("kernel32.dll")] private static extern nint LoadResource(nint module, nint resource);
    [DllImport("kernel32.dll")] private static extern nint LockResource(nint resource);
    [DllImport("user32.dll")] private static extern nint SendMessage(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("user32.dll")] private static extern bool PrintWindow(nint hwnd, nint dc, uint flags);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleBitmap(nint dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
}
