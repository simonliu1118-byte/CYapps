using System.Windows;
using System.Windows.Controls;

namespace CYEnvelope;

public sealed class FrameWindow : Window
{
    private readonly AppSettings _settings;
    private readonly List<string> _working;
    private readonly ListBox _list = new() { Height = 155 };
    private readonly TextBox _entry = new() { Margin = new Thickness(0, 8, 0, 8) };
    public FrameWindow(AppSettings settings)
    {
        _settings = settings;
        _working = settings.FrameTexts.ToList();
        Title = "方框文字";
        Width = 380; Height = 330; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new System.Windows.Media.FontFamily("Microsoft JhengHei UI");
        FontSize = 14;
        var root = new StackPanel { Margin = new Thickness(16) };
        Content = root;
        root.Children.Add(new TextBlock { Text = "選擇文字後可修改；新增文字請先清空輸入欄。" });
        root.Children.Add(_list);
        root.Children.Add(_entry);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        root.Children.Add(row);
        foreach (var (title, action) in new (string, RoutedEventHandler)[]
        {
            ("新增", Add), ("修改", Edit), ("刪除", Delete), ("完成", Done)
        })
        {
            var b = new Button { Content = title, MinWidth = 70, Margin = new Thickness(0, 0, 8, 0) };
            b.Click += action; row.Children.Add(b);
        }
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
        if (MessageBox.Show("修改這筆方框文字？", "確認", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        _working[_list.SelectedIndex] = _entry.Text.Trim();
        Refresh();
    }
    private void Delete(object sender, RoutedEventArgs e)
    {
        if (_list.SelectedIndex < 0) return;
        if (MessageBox.Show("刪除這筆方框文字？", "確認", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        _working.RemoveAt(_list.SelectedIndex);
        Refresh(); _entry.Clear();
    }
    private void Done(object sender, RoutedEventArgs e)
    {
        _settings.FrameTexts = _working;
        DialogResult = true; Close();
    }
}
