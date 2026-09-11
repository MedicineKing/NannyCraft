using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using McLauncher.Services;
using Wpf.Ui.Appearance;

namespace McLauncher;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 未处理异常 → 崩溃日志(反馈时自动附带)
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogService.WriteCrash(args.ExceptionObject as Exception ?? new Exception("未知异常"), "后台线程未处理异常");

        LogService.Info($"启动 {Models.AppInfo.DisplayName} {Models.AppInfo.VersionText}(构建 {Models.AppInfo.BuildDate})");

        // Fluent 深色主题 + 用户主题色(默认拂晓蓝)
        ApplicationThemeManager.Apply(ApplicationTheme.Dark);

        var color = Color.FromRgb(0x5B, 0x8C, 0xF5);
        try
        {
            var settings = SettingsStore.Load();
            if (!string.IsNullOrWhiteSpace(settings.AccentColor) && ColorConverter.ConvertFromString(settings.AccentColor) is Color parsed)
                color = parsed;
            // 动画速度:在窗口解析前注入,保证 XAML 里的时长也取到倍率
            Motion.Motion.SetSpeed(settings.AnimationSpeed <= 0 ? 1.0 : settings.AnimationSpeed);
        }
        catch (Exception ex) { LogService.Error("读取设置失败", ex); }
        ApplicationAccentColorManager.Apply(color, ApplicationTheme.Dark);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogService.WriteCrash(e.Exception, "UI 线程未处理异常");
        MessageBox.Show(
            $"启动器遇到错误,崩溃日志已保存:\n{LogService.LastCrashPath}\n\n可在「关于 → 帮助与反馈」一键反馈(日志会自动附上)。",
            "mc-launcher", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
        Shutdown();
    }
}
