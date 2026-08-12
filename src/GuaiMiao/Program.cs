using System.Windows;
using GuaiMiao.Infrastructure;
using GuaiMiao.Diagnostics;
using GuaiMiao.Services;

namespace GuaiMiao;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains(AppInfo.UninstallHelperArgument, StringComparer.OrdinalIgnoreCase))
            return SelfInstaller.RunUninstallHelper(args);
        if (args.Contains(AppInfo.SelfTestArgument, StringComparer.OrdinalIgnoreCase))
            return SelfTest.Run(GetArgumentValue(args, AppInfo.SelfTestArgument) ??
                                Path.Combine(Path.GetTempPath(), "guai-miao-self-test.json"));

        try
        {
            LocalLog.Initialize();
            var bootstrap = SelfInstaller.Bootstrap(args);
            if (bootstrap.ShouldExit)
                return 0;

            using var singleInstance = SingleInstanceService.TryAcquire();
            if (singleInstance is null)
            {
                SingleInstanceService.SendAsync("attention").GetAwaiter().GetResult();
                return 0;
            }

            var application = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            application.DispatcherUnhandledException += (_, eventArgs) =>
            {
                LocalLog.Error("ui-unhandled", eventArgs.Exception);
                System.Windows.MessageBox.Show($"乖喵遇到错误：{eventArgs.Exception.Message}", AppInfo.ProductName,
                    MessageBoxButton.OK, MessageBoxImage.Error);
                eventArgs.Handled = true;
            };

            using var controller = new AppController(application, bootstrap, singleInstance);
            controller.Start(GetArgumentValue(args, "--health-token"));
            application.Run();
            return 0;
        }
        catch (Exception ex)
        {
            LocalLog.Error("startup-failed", ex);
            System.Windows.MessageBox.Show($"乖喵无法启动：{ex.Message}", AppInfo.ProductName,
                MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }

    private static string? GetArgumentValue(IReadOnlyList<string> args, string name)
    {
        for (var index = 0; index + 1 < args.Count; index++)
            if (args[index].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[index + 1];
        return null;
    }
}
