using System.Windows;
using System.Windows.Input;
using McLauncher.Services;
using McLauncher.ViewModels;
using Wpf.Ui.Controls;

namespace McLauncher.Views;

/// 「游戏设置」窗口:语言 / 常用选项(模糊搜索)/ 键位(新版命名 ↔ 旧版数字键码)。
public partial class GameSettingsWindow : FluentWindow
{
    private readonly GameSettingsViewModel _vm;
    private SettingRow? _capturing;

    public GameSettingsWindow(string gameRoot)
    {
        LogService.Info("游戏设置窗口:构造中");
        InitializeComponent();
        _vm = new GameSettingsViewModel(gameRoot);
        DataContext = _vm;
        PreviewKeyDown += OnCaptureKey;
        PreviewMouseDown += OnCaptureMouse;
        Loaded += (_, _) => LogService.Info("游戏设置窗口:已显示");
        Closed += (_, _) => LogService.Info("游戏设置窗口:已关闭");
    }

    private void OnKeybindClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SettingRow row }) return;
        _capturing = row;
        _vm.StatusText = $"按下要绑定的按键…({row.Label};Esc 取消)";
    }

    /// 改键捕获:键盘
    private void OnCaptureKey(object sender, KeyEventArgs e)
    {
        if (_capturing == null) return;
        if (e.Key == Key.Escape)
        {
            _capturing = null;
            _vm.StatusText = "已取消改键";
            e.Handled = true;
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        FinishCapture(OptionCatalog.FromWpfKey(key, _vm.UsesLegacyKeys));
        e.Handled = true;
    }

    /// 改键捕获:鼠标键(左右中键常用于攻击/使用)
    private void OnCaptureMouse(object sender, MouseButtonEventArgs e)
    {
        if (_capturing == null) return;
        var button = e.ChangedButton switch
        {
            MouseButton.Left => 0,
            MouseButton.Right => 1,
            MouseButton.Middle => 2,
            _ => -1,
        };
        if (button < 0) return;

        FinishCapture(OptionCatalog.MouseValue(button, _vm.UsesLegacyKeys));
        e.Handled = true;
    }

    private void FinishCapture(string? value)
    {
        if (_capturing == null) return;
        if (string.IsNullOrEmpty(value))
        {
            _vm.StatusText = "这个按键无法用于当前版本的键位格式(换一个试试)";
            return;
        }

        _capturing.TextValue = value;
        _capturing.KeyDisplay = OptionCatalog.DisplayKey(value);
        _vm.StatusText = $"{_capturing.Label} → {_capturing.KeyDisplay}(记得点保存)";
        _capturing = null;
    }

    /// 点选搜索候选:筛到该行并收起下拉
    private void OnSuggestionClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SettingRow row })
            _vm.PickSuggestion(row);
    }

    private void OnSaveClick(object sender, RoutedEventArgs e) => _vm.Save();

    private void OnReloadClick(object sender, RoutedEventArgs e) => _vm.Reload();
}
