using System.Windows;
using System.Windows.Controls;

namespace CYEnvelope;

public sealed class SettingsWindow : Window
{
    public SettingsWindow(AppSettings settings)
    {
        Title = "設定"; Width = 420; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        DesktopUi.Dialog(this);
        var panel = new StackPanel { Margin = new Thickness(20) };
        Content = panel;
        panel.Children.Add(DesktopUi.Heading("輸入方式"));
        var direct = new CheckBox { Content = "直接點選信封欄位輸入", IsChecked = settings.DirectEntry };
        panel.Children.Add(direct);
        panel.Children.Add(new TextBlock { Text = "關閉後，使用主畫面左側的收件資料欄。", TextWrapping = TextWrapping.Wrap,
            Foreground = DesktopUi.Muted, Margin = new Thickness(0, 8, 0, 24) });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = DesktopUi.Action("取消", (_, _) => Close()); cancel.IsCancel = true;
        actions.Children.Add(cancel);
        var save = DesktopUi.Action("儲存", (_, _) => { settings.DirectEntry = direct.IsChecked == true; DialogResult = true; }, true);
        save.IsDefault = true; save.Margin = new Thickness(0); actions.Children.Add(save);
        panel.Children.Add(actions);
    }
}
