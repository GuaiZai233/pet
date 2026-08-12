using System.Windows;
using System.Windows.Controls;

namespace GuaiMiao;

internal sealed class UninstallWindow : Window
{
    private readonly System.Windows.Controls.CheckBox _deleteSettings;

    public UninstallWindow()
    {
        Title = $"卸载 {AppInfo.ProductName}";
        Width = 420;
        Height = 220;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        Topmost = true;

        var root = new StackPanel { Margin = new Thickness(28) };
        root.Children.Add(new TextBlock
        {
            Text = "将移除已安装的乖喵程序、自启动项、缓存和本地日志。原始下载的 EXE 不会删除。",
            FontSize = 15,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 18)
        });
        _deleteSettings = new System.Windows.Controls.CheckBox
        {
            Content = "同时删除位置和偏好设置",
            IsChecked = true,
            Margin = new Thickness(0, 0, 0, 24)
        };
        root.Children.Add(_deleteSettings);

        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var cancel = new System.Windows.Controls.Button { Content = "取消", Width = 88, Height = 32, IsCancel = true };
        cancel.Click += (_, _) => Close();
        var uninstall = new System.Windows.Controls.Button
        {
            Content = "继续卸载",
            Width = 100,
            Height = 32,
            IsDefault = true,
            Margin = new Thickness(12, 0, 0, 0)
        };
        uninstall.Click += (_, _) => { DialogResult = true; Close(); };
        buttons.Children.Add(cancel);
        buttons.Children.Add(uninstall);
        root.Children.Add(buttons);
        Content = root;
    }

    public bool DeleteSettings => _deleteSettings.IsChecked == true;
}
