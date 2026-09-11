using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using McLauncher.Models;
using McLauncher.ViewModels;
using Wpf.Ui.Controls;
using MotionSpec = McLauncher.Motion.Motion;

namespace McLauncher;

/// 主窗口:页面切换 / 详情刷新动画统一经 MotionSpec(150/220/350 三档 + 标准贝塞尔 + 减少动画降级)。
/// 窗口外壳为 WPF-UI FluentWindow(标题栏仅最小化/关闭)。
public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        _currentPage = PageHome;
        Loaded += OnLoaded;
        _vm.PropertyChanged += OnVmPropertyChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        PlayEnter(Root, 12, MotionSpec.Slow); // 进入动画:350ms 淡入 + 上移 12px(大位移档)
        await _vm.LoadAsync();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.CurrentPage):
                SwitchPage();
                break;
            case nameof(MainViewModel.SelectedDetail):
                if (FindName("DetailCard") is UIElement card)
                    PlayEnter(card, 8, MotionSpec.Normal); // 详情刷新:220ms 淡入 + 上移 8px(标准档)
                break;
        }
    }

    private void OnNavChecked(object sender, RoutedEventArgs e)
    {
        // 导航用 Tag 存图标码点;页面按控件名分发(Content 已是本地化文案,不能再按中文匹配)
        if (sender is not RadioButton button) return;
        _vm.CurrentPage = button.Name switch
        {
            "NavDownload" => AppPage.Download,
            "NavSettings" => AppPage.Settings,
            "NavAbout" => AppPage.About,
            _ => AppPage.Home,
        };
    }

    private void OnInstallLinkClick(object sender, RoutedEventArgs e)
        => _vm.CurrentPage = AppPage.Download;

    /// 色板点击:走命令(Checked 事件比 RelativeSource 命令绑定可靠,且避免重复触发)
    private void OnAccentChecked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { DataContext: AccentOption option } && !option.IsSelected)
            _vm.SelectAccentCommand.Execute(option);
    }

    /// 下载页版本类型筛选(正式版 / 快照 / 愚人节 / 远古;枚举放 Uid,Tag 让给图标码点)
    private void OnVersionKindChecked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Uid: { Length: > 0 } uid } && Enum.TryParse<VersionKindFilter>(uid, out var kind))
            _vm.VersionKind = kind;
    }

    /// 设置页分类(左列):切换五个内容面板。
    /// 注意:首个分类 IsChecked=True 会在 InitializeComponent 期间触发,此时后面的面板字段尚未建好 → 判空直接返回(默认可见性已在 XAML 里写好)。
    private void OnSettingsCategoryChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Uid: { Length: > 0 } uid }) return;
        if (SetCatGame is null || SetCatLook is null || SetCatIsolate is null || SetCatNews is null || SetCatUpdate is null)
            return;
        SetCatGame.Visibility = uid == "Game" ? Visibility.Visible : Visibility.Collapsed;
        SetCatLook.Visibility = uid == "Look" ? Visibility.Visible : Visibility.Collapsed;
        SetCatIsolate.Visibility = uid == "Isolate" ? Visibility.Visible : Visibility.Collapsed;
        SetCatNews.Visibility = uid == "News" ? Visibility.Visible : Visibility.Collapsed;
        SetCatUpdate.Visibility = uid == "Update" ? Visibility.Visible : Visibility.Collapsed;
    }

    /// 版本同步:把版本设为隔离(走 Click 事件,列表项里的 RelativeSource 命令绑定有静默失效前例)
    private void OnIsolateClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: VersionEntry version })
            _vm.SetIsolated(version.Id, true);
    }

    /// 版本同步:把版本恢复为参与同步(共享游戏目录)
    private void OnRestoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: VersionEntry version })
            _vm.SetIsolated(version.Id, false);
    }

    private FrameworkElement? _currentPage;

    private void SwitchPage()
    {
        FrameworkElement? target = _vm.CurrentPage switch
        {
            AppPage.Download => PageDownload,
            AppPage.Settings => PageSettings,
            AppPage.About => PageAbout,
            _ => PageHome,
        };
        if (target == null) return;

        // 导航选中态与当前页保持同步(含程序化跳转,如空状态里的"安装"链接)
        NavHome.IsChecked = _vm.CurrentPage == AppPage.Home;
        NavDownload.IsChecked = _vm.CurrentPage == AppPage.Download;
        NavSettings.IsChecked = _vm.CurrentPage == AppPage.Settings;
        NavAbout.IsChecked = _vm.CurrentPage == AppPage.About;

        if (ReferenceEquals(target, _currentPage))
        {
            target.Visibility = Visibility.Visible;
            return;
        }

        // 交叉转场:旧页 220ms 淡出,新页 220ms 淡入 + 上移 8px(标准档,不再"啪"地切换)
        var previous = _currentPage;
        _currentPage = target;
        target.Visibility = Visibility.Visible;

        if (previous != null)
        {
            var fadeOut = new DoubleAnimation(previous.Opacity, 0, MotionSpec.DurationFor(MotionSpec.Normal))
            {
                // 出场用退出曲线(快出)、入场用标准曲线(缓入):进出分曲线,转场更顺
                EasingFunction = MotionSpec.ExitEase,
                FillBehavior = FillBehavior.Stop,
            };
            var old = previous;
            fadeOut.Completed += (_, _) =>
            {
                old.Visibility = Visibility.Collapsed;
                old.Opacity = 1;
            };
            previous.BeginAnimation(OpacityProperty, fadeOut);
        }

        PlayEnter(target, 8, MotionSpec.Normal);
    }

    /// 通用入场动画:透明度 0→1 + 位移 dy→0;时长经 MotionSpec(系统减少动画时归零)。
    /// 基准值必须归零(位移/透明度),否则 FillBehavior.Stop 结束后会回弹到基准值残留 8px 错位。
    private static void PlayEnter(UIElement element, double dy, Duration desired)
    {
        var duration = MotionSpec.DurationFor(desired);
        element.Opacity = 0;
        element.RenderTransform = new TranslateTransform(0, 0);

        var storyboard = new Storyboard();

        var fade = new DoubleAnimation(0, 1, duration)
        {
            EasingFunction = MotionSpec.StandardEase,
            FillBehavior = FillBehavior.HoldEnd,
        };
        Storyboard.SetTarget(fade, element);
        Storyboard.SetTargetProperty(fade, new PropertyPath(UIElement.OpacityProperty));

        var move = new DoubleAnimation(dy, 0, duration)
        {
            EasingFunction = MotionSpec.StandardEase,
            FillBehavior = FillBehavior.HoldEnd,
        };
        Storyboard.SetTarget(move, element);
        Storyboard.SetTargetProperty(move,
            new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));

        storyboard.Children.Add(fade);
        storyboard.Children.Add(move);
        storyboard.Begin();
    }
}
