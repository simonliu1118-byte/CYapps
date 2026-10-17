using System.Windows;
using System.Windows.Controls;

namespace CYEnvelope;

public sealed class FrameWindow : Window
{
    private readonly AppSettings _settings;
    private readonly List<string> _working;
    private readonly ListBox _list = new() { MinHeight = 140 };
    private readonly TextBox _entry = new() { Margin = new Thickness(0, 8, 0, 8) };
    public FrameWindow(AppSettings settings)
    {
        _settings = settings;
        _working = settings.FrameTexts.ToList();
        Title = "方框文字";
        Width = 480; Height = 410; MinWidth = 440; MinHeight = 370;
        DesktopUi.Dialog(this);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new System.Windows.Media.FontFamily("Microsoft JhengHei UI");
        FontSize = 14;
        var root = new DockPanel { Margin = new Thickness(20) };
        Content = root;
        var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        heading.Children.Add(DesktopUi.Heading("方框文字"));
        heading.Children.Add(new TextBlock { Text = "選取項目後可修改；新增時直接輸入新文字。", TextWrapping = TextWrapping.Wrap,
            Foreground = DesktopUi.Muted, Margin = new Thickness(0, 0, 0, 14) });
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        footer.Children.Add(_entry);
        var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); footer.Children.Add(row);
        var edit = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(edit);
        edit.Children.Add(DesktopUi.Action("新增", Add));
        edit.Children.Add(DesktopUi.Action("修改", Edit));
        edit.Children.Add(DesktopUi.Action("刪除", Delete));
        var done = DesktopUi.Action("完成", Done, true); done.Margin = new Thickness(0);
        Grid.SetColumn(done, 1); row.Children.Add(done);
        root.Children.Add(_list);
        _list.SelectionChanged += (_, _) => { if (_list.SelectedItem is string s) _entry.Text = s; };
        Refresh();
    }
    private void Refresh() => _list.ItemsSource = _working.ToArray();
    private void Add(object sender, RoutedEventArgs e)
    {
        var value = _entry.Text.Trim();
        if (value.Length == 0 || _working.Contains(value)) return;
        _working.Add(value);
        Refresh(); _entry.Clear();
    }
    private void Edit(object sender, RoutedEventArgs e)
    {
        if (_list.SelectedIndex < 0 || _entry.Text.Trim().Length == 0) return;
        if (MessageBox.Show(this, "修改這筆方框文字？", "確認修改", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _working[_list.SelectedIndex] = _entry.Text.Trim();
        Refresh();
    }
    private void Delete(object sender, RoutedEventArgs e)
    {
        if (_list.SelectedIndex < 0) return;
        if (MessageBox.Show(this, "刪除這筆方框文字？", "確認刪除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _working.RemoveAt(_list.SelectedIndex);
        Refresh(); _entry.Clear();
    }
    private void Done(object sender, RoutedEventArgs e)
    {
        _settings.FrameTexts = _working;
        DialogResult = true; Close();
    }
}
