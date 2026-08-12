using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using GuaiMiao.Infrastructure;
using GuaiMiao.Services;

namespace GuaiMiao;

internal sealed class AboutWindow : Window
{
    private readonly GitHubUpdateService _updateService = new();
    private readonly System.Windows.Controls.Button _checkUpdate;
    private readonly TextBlock _updateStatus;

    public AboutWindow()
    {
        Title = $"关于 {AppInfo.ProductName}";
        Width = 410;
        Height = 300;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        Topmost = true;
        Background = System.Windows.Media.Brushes.White;

        var panel = new StackPanel { Margin = new Thickness(28) };
        panel.Children.Add(new TextBlock
        {
            Text = AppInfo.AboutText,
            FontSize = 16,
            Margin = new Thickness(0, 4, 0, 20),
            TextWrapping = TextWrapping.Wrap
        });

        var linkText = new TextBlock { FontSize = 15, Margin = new Thickness(0, 0, 0, 14) };
        var link = new Hyperlink { NavigateUri = new Uri(AppInfo.HomepageUrl) };
        link.Inlines.Add(AppInfo.HomepageLabel);
        link.RequestNavigate += (_, _) => Process.Start(new ProcessStartInfo
        {
            FileName = AppInfo.HomepageUrl,
            UseShellExecute = true
        });
        linkText.Inlines.Add(link);
        panel.Children.Add(linkText);

        panel.Children.Add(new TextBlock
        {
            Text = $"当前版本：{GitHubUpdateService.CurrentVersion.ToString(3)}",
            FontSize = 13,
            Foreground = System.Windows.Media.Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 8)
        });

        _updateStatus = new TextBlock
        {
            Text = "仅在点击按钮时连接 GitHub 检查更新。",
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        panel.Children.Add(_updateStatus);

        var buttons = new DockPanel { LastChildFill = false };
        _checkUpdate = new System.Windows.Controls.Button
        {
            Content = "检查更新",
            Width = 100,
            Height = 32,
            Margin = new Thickness(0, 0, 10, 0)
        };
        _checkUpdate.Click += CheckUpdate;
        buttons.Children.Add(_checkUpdate);

        var close = new System.Windows.Controls.Button
        {
            Content = "关闭",
            Width = 92,
            Height = 32,
            IsDefault = true,
            IsCancel = true
        };
        DockPanel.SetDock(close, Dock.Right);
        close.Click += (_, _) => Close();
        buttons.Children.Add(close);
        panel.Children.Add(buttons);
        Content = panel;
    }

    private async void CheckUpdate(object sender, RoutedEventArgs e)
    {
        _checkUpdate.IsEnabled = false;
        _updateStatus.Text = "正在从 GitHub 获取最新版本…";
        try
        {
            var result = await _updateService.CheckAsync();
            if (!result.IsUpdateAvailable)
            {
                _updateStatus.Text = $"当前已经是最新版本（{result.CurrentVersion.ToString(3)}）。";
                return;
            }

            _updateStatus.Text = $"发现新版本 {result.LatestVersion.ToString(3)}。";
            if (result.DownloadUrl is null || result.Sha256 is null)
            {
                var openRelease = System.Windows.MessageBox.Show(
                    "该发布缺少“乖喵.exe”或 SHA-256 摘要，无法安全自动更新。是否打开 GitHub 发布页面？",
                    $"发现乖喵 {result.LatestVersion.ToString(3)}",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.Yes);
                if (openRelease == MessageBoxResult.Yes)
                    GitHubUpdateService.OpenUrl(result.ReleasePageUrl);
                return;
            }

            var choice = System.Windows.MessageBox.Show(
                "是否立即从 GitHub 下载、校验并安装新版？程序会验证 Release 的 SHA-256，成功后启动现有升级与回滚流程。",
                $"发现乖喵 {result.LatestVersion.ToString(3)}",
                MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.Yes);
            if (choice == MessageBoxResult.Yes)
            {
                _updateStatus.Text = "正在下载并校验更新…";
                var installer = await _updateService.DownloadAsync(result);
                _updateStatus.Text = "校验通过，正在启动升级…";
                GitHubUpdateService.LaunchInstaller(installer);
            }
        }
        catch (Exception ex)
        {
            LocalLog.Warn("update-check-failed", ex);
            _updateStatus.Text = $"检查更新失败：{ex.Message}";
        }
        finally
        {
            _checkUpdate.IsEnabled = true;
        }
    }
}
