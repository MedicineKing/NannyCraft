using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using McLauncher.Models;
using McLauncher.Services;

namespace McLauncher.ViewModels;

public enum AppPage { Home, Download, Settings, About }

/// 下载页版本类型筛选
public enum VersionKindFilter { Release, Snapshot, AprilFools, Ancient }

/// 主视图模型:PCL2 式四页导航(主页 / 下载 / 设置 / 关于)。
public partial class MainViewModel : ObservableObject
{
    /// 设置脏标记:有未保存的改动时「保存设置」才点亮(没改过=置灰)
    [ObservableProperty] private bool _settingsDirty;
    private bool _suppressDirty; // 加载期间的赋值不算"改动"

    /// 只有经「保存设置」落盘的字段才参与脏标记(其余项是即改即存)
    private static readonly HashSet<string> DirtyTrackedFields = new()
    {
        nameof(MaxMemoryMb), nameof(GameRoot), nameof(Username), nameof(JvmArgs),
        nameof(NewsSourceUrl), nameof(NewsProxy), nameof(NewsTranslate), nameof(RememberLastVersion),
    };

    public MainViewModel()
    {
        PropertyChanged += (_, e) =>
        {
            if (!_suppressDirty && e.PropertyName != null && DirtyTrackedFields.Contains(e.PropertyName))
                SettingsDirty = true;
        };
    }

    private readonly ManifestService _manifest = new();
    private readonly JavaService _java = new();
    private readonly LaunchService _launch = new();
    private readonly DownloadService _download = new();
    private readonly InstallService _install = new();
    private readonly UpdateService _update = new();
    private readonly NewsService _news = new();
    private readonly JdkService _jdk = new();
    private LauncherSettings _settings = new();

    private List<VersionEntry> _allVersions = new();

    public ObservableCollection<VersionEntry> Versions { get; } = new();

    /// 主页列表:仅本机已安装(versions/<id>/<id>.jar 存在)的版本
    public ObservableCollection<VersionEntry> InstalledVersions { get; } = new();

    /// 版本同步维护表:参与同步(共享游戏目录)/ 已隔离(独立目录,不参与同步)
    public ObservableCollection<VersionEntry> SyncedVersions { get; } = new();
    public ObservableCollection<VersionEntry> IsolatedVersionEntries { get; } = new();

    public ObservableCollection<JavaRuntime> Javas { get; } = new();

    // ── 导航 ──
    [ObservableProperty] private AppPage _currentPage = AppPage.Home;
    [ObservableProperty] private string _pageTitle = "主页";

    partial void OnCurrentPageChanged(AppPage value)
    {
        RefreshPageTitle();
        if (value == AppPage.Home)
            RefreshInstalled(); // 回主页时刷新本机已安装列表
    }

    private void RefreshPageTitle() => PageTitle = CurrentPage switch
    {
        AppPage.Download => Loc.T("S_NavDownload"),
        AppPage.Settings => Loc.T("S_NavSettings"),
        AppPage.About => Loc.T("S_NavAbout"),
        _ => Loc.T("S_NavHome"),
    };

    // ── 设置 · 启动器语言 ──
    public IReadOnlyList<LanguageOption> LauncherLanguages { get; } =
        Loc.Languages.Select(l => new LanguageOption(l.Code, l.Name)).ToList();

    [ObservableProperty] private LanguageOption? _selectedLanguage;

    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (value == null) return;
        _settings.LauncherLanguage = value.Code;
        SettingsStore.Save(_settings);
        Loc.Apply(value.Code);
        RefreshPageTitle();
        OnPropertyChanged(nameof(HeroTitle));
        RefreshLocalizedLists(); // 预设列表/模型文案(通道/速度/Java 来源/版本类型)重建刷新
        LauncherIdText = $"{Loc.T("S_LauncherIdPrefix")}: {LauncherId}";
        if (_settings.GameLanguageFollows) SyncGameLanguage(); // 实时跟随:界面上改语言,游戏也跟着改
    }

    /// 把启动器语言写进共享目录 options.txt(仅"跟随启动器"且非隔离版本;大小写风格沿用原文件)
    private void SyncGameLanguage()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(GameRoot)) return;
            var file = new GameOptionsFile(Path.Combine(GameRoot, "options.txt"));
            var code = Loc.MinecraftLangCode;
            var old = file.Get("lang") ?? "";
            var value = old.Length > 0 && old == old.ToLowerInvariant() ? code.ToLowerInvariant() : code;
            if (old == value) return;
            file.Set("lang", value);
            file.Save();
            LogService.Info($"游戏语言已跟随启动器:{value}");
        }
        catch (Exception ex)
        {
            LogService.Warn("写入游戏语言失败:" + ex.Message);
        }
    }

    // ── 版本隔离:名单内版本用独立游戏目录(versions/<id>),不参与同步 ──

    public bool IsIsolated(string versionId) =>
        _settings.IsolatedVersions.Any(v => string.Equals(v, versionId, StringComparison.OrdinalIgnoreCase));

    public string EffectiveGameDir(string versionId) =>
        !string.IsNullOrWhiteSpace(versionId) && IsIsolated(versionId)
            ? Path.Combine(GameRoot, "versions", versionId)
            : GameRoot;

    public void SetIsolated(string versionId, bool isolated)
    {
        var exists = IsIsolated(versionId);
        if (isolated && !exists) _settings.IsolatedVersions.Add(versionId);
        if (!isolated && exists)
            _settings.IsolatedVersions.RemoveAll(v => string.Equals(v, versionId, StringComparison.OrdinalIgnoreCase));
        SettingsStore.Save(_settings);
        LogService.Info($"{(isolated ? "启用" : "关闭")}版本隔离:{versionId}");
        RefreshInstalled(); // 立即在两栏之间移动(重建列表并保持选中)
        RefreshCheck();
    }

    /// 默认游戏目录:优先用官方启动器的 .minecraft(PCL 同款策略),没有才回落自带目录
    private static string DetectDefaultGameRoot()
    {
        try
        {
            var mc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
            if (Directory.Exists(mc)) return mc;
        }
        catch { /* 探测失败用默认 */ }
        return SettingsStore.DefaultGameRoot;
    }

    /// 选择游戏目录(PCL 式文件夹选择器,不再手输路径)
    [RelayCommand]
    private void ChooseGameFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog();
        if (dialog.ShowDialog() == true) GameRoot = dialog.FolderName;
    }

    /// 清华镜像连通性自动探测(用户不再手选):通 → 内置 Java 下载走镜像,否则回退官方
    private async Task ProbeTunaMirrorAsync()
    {
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            using var resp = await http.SendAsync(new System.Net.Http.HttpRequestMessage(
                System.Net.Http.HttpMethod.Get, "https://mirrors.tuna.tsinghua.edu.cn/"));
            UseTunaJdkMirror = resp.IsSuccessStatusCode;
        }
        catch { UseTunaJdkMirror = false; }
        LogService.Info($"清华镜像连通性探测:{UseTunaJdkMirror}");
    }

    // ── 主页 / 下载 ──
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "正在加载版本列表…";
    [ObservableProperty] private bool _showProgress;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private VersionKindFilter _versionKind = VersionKindFilter.Release;
    [ObservableProperty] private VersionEntry? _selectedVersion;
    [ObservableProperty] private VersionDetail? _selectedDetail;
    [ObservableProperty] private JavaRuntime? _selectedJava;
    [ObservableProperty] private string _javaHint = "";
    [ObservableProperty] private string _checkText = "";
    [ObservableProperty] private bool _canLaunch;
    [ObservableProperty] private string _launchButtonText = "开始游戏";

    // ── 设置 ──
    [ObservableProperty] private int _maxMemoryMb = 4096;
    [ObservableProperty] private string _gameRoot = "";
    [ObservableProperty] private string _jvmArgs = "";
    [ObservableProperty] private string _username = "Player";
    [ObservableProperty] private bool _autoConcurrency = true;
    [ObservableProperty] private int _downloadConcurrency = 256;
    [ObservableProperty] private string _settingsSavedHint = "";

    /// 自动并发:按 CPU 核心数自适应(4 核 64 → 32 核 512),卡在 64-512 区间
    public int AutoConcurrencyValue => Math.Clamp(Environment.ProcessorCount * 16, 64, 512);
    /// 实际生效的并发数
    public int EffectiveConcurrency => AutoConcurrency ? AutoConcurrencyValue : Math.Clamp(DownloadConcurrency, 16, 1024);
    /// 实际并发数文案(带"线程"单位,随语言本地化)
    public string EffectiveConcurrencyText => Loc.F("S_DlThreads", EffectiveConcurrency);
    /// 手动模式下滑杆才可用
    public bool ManualConcurrencyEnabled => !AutoConcurrency;

    partial void OnAutoConcurrencyChanged(bool value)
    {
        OnPropertyChanged(nameof(EffectiveConcurrency));
        OnPropertyChanged(nameof(EffectiveConcurrencyText));
        OnPropertyChanged(nameof(ManualConcurrencyEnabled));
        _settings.DownloadConcurrency = value ? 0 : Math.Clamp(DownloadConcurrency, 16, 1024);
        SettingsStore.Save(_settings);
    }

    partial void OnDownloadConcurrencyChanged(int value)
    {
        if (AutoConcurrency) return;
        OnPropertyChanged(nameof(EffectiveConcurrency));
        OnPropertyChanged(nameof(EffectiveConcurrencyText));
        _settings.DownloadConcurrency = Math.Clamp(value, 16, 1024);
        SettingsStore.Save(_settings);
    }

    // ── 主页空状态 ──
    [ObservableProperty] private bool _showEmptyState;

    /// 主页 Hero 标题:未选版本时显示本地化占位(TargetNullValue 吃不了动态资源,走 VM 计算)
    public string HeroTitle => SelectedVersion?.Id ?? Loc.T("S_NoVersionSelected");

    // ── 主页 · 详情面板(未选版本时:快讯 / 提示) ──
    public ObservableCollection<NewsItem> News { get; } = new();
    [ObservableProperty] private bool _hasSelectedVersion;

    /// 下载页右侧:未选版本时显示提示、不显示安装按钮
    public bool HasNoSelection => !HasSelectedVersion;
    [ObservableProperty] private bool _showNewsOnIdle = true;
    [ObservableProperty] private string _newsSourceUrl = "";
    [ObservableProperty] private string _newsProxy = "";
    [ObservableProperty] private bool _newsTranslate = true;
    [ObservableProperty] private string _feedbackRepo = "";
    [ObservableProperty] private string _launcherIdText = "";
    public string LauncherId { get; private set; } = "";

    // ── 内置 Java 运行时 ──
    [ObservableProperty] private bool _needsJavaDownload;
    [ObservableProperty] private string _javaDownloadText = "";
    [ObservableProperty] private bool _useTunaJdkMirror;

    partial void OnUseTunaJdkMirrorChanged(bool value)
    {
        _settings.UseTunaJdkMirror = value;
        SettingsStore.Save(_settings);
    }

    [RelayCommand]
    private async Task DownloadJavaAsync()
    {
        var version = SelectedDetail?.JavaVersion is { MajorVersion: > 0 } hint ? hint.MajorVersion : 21;
        IsBusy = true;
        ShowProgress = true;
        Progress = 0;
        try
        {
            StatusText = Loc.F("S_Msg_JavaDownloading", version);
            var reporter = new Progress<double>(p => Progress = p * 100);
            var javaExe = await _jdk.EnsureAsync(version, UseTunaJdkMirror, reporter);
            if (javaExe == null)
            {
                StatusText = Loc.F("S_Msg_JavaFailed", version);
                return;
            }
            StatusText = Loc.F("S_Msg_JavaReady", version, javaExe);
            ScanJava();
            SelectedJava = Javas.FirstOrDefault(j => j.Version >= version) ?? SelectedJava;
            RefreshCheck();
        }
        catch (Exception ex)
        {
            StatusText = "Java 下载失败:" + ex.Message;
            LogService.Error($"下载 Java {version} 失败", ex);
        }
        finally
        {
            ShowProgress = false;
            IsBusy = false;
        }
    }

    // ── 关于 · 帮助与反馈 ──

    [RelayCommand]
    private async Task ReportBugAsync() => await OpenFeedbackAsync(FeedbackKind.Bug);

    [RelayCommand]
    private async Task SuggestIdeaAsync() => await OpenFeedbackAsync(FeedbackKind.Idea);

    /// QQ 群反馈:上不了 GitHub 的用户的备用通道
    [RelayCommand]
    private void OpenQQGroup()
    {
        if (string.IsNullOrWhiteSpace(AppInfo.QQGroupUrl))
        {
            StatusText = Loc.T("S_Fb_QQNotSet");
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppInfo.QQGroupUrl) { UseShellExecute = true });
            LogService.Info("打开 QQ 群反馈链接");
        }
        catch (Exception ex) { StatusText = Loc.F("S_Fb_OpenFailed", ex.Message); }
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(LogService.LogDir);
            System.Diagnostics.Process.Start("explorer.exe", LogService.LogDir);
        }
        catch (Exception ex) { StatusText = Loc.F("S_OpenLogFailed", ex.Message); }
    }

    /// 反馈入口:先过版本闸门(非最新版先引导更新——旧版本的问题可能已修),
    /// 通过后打开预填好的 GitHub issue 页(环境/日志自动填;正文长度已做预算,不会触发 414)。
    private async Task OpenFeedbackAsync(FeedbackKind kind)
    {
        try
        {
            var check = await _update.CheckAsync(SelectedChannel?.Channel ?? UpdateChannel.Release, AppInfo.VersionText);
            if (check.Checked && check.HasUpdate)
            {
                var go = System.Windows.MessageBox.Show(
                    Loc.F("S_Fb_NeedUpdate", check.LatestVersion),
                    Loc.T("S_Fb_NeedUpdateTitle"),
                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
                if (go == System.Windows.MessageBoxResult.Yes)
                {
                    CurrentPage = AppPage.Settings; // 设置 → 版本与更新
                    StatusText = Loc.T("S_Fb_UpdateFirst");
                }
                return;
            }
        }
        catch { /* 检查失败(离线/服务器未就绪)→ 不拦截,直接放行 */ }

        var repo = FeedbackRepo.Trim();
        if (string.IsNullOrWhiteSpace(repo)) repo = AppInfo.RepoUrl;
        if (string.IsNullOrWhiteSpace(repo))
        {
            StatusText = "还没配置反馈仓库:设置 → 资讯卡片里填「反馈仓库地址」";
            return;
        }

        try
        {
            var javaText = SelectedJava != null ? $"Java {SelectedJava.Version}({SelectedJava.Home})" : "未探测到";
            var url = FeedbackService.BuildIssueUrl(repo, kind, LauncherId, javaText);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            LogService.Info($"打开反馈页({kind})");
            StatusText = Loc.T("S_Fb_Opened");
        }
        catch (Exception ex)
        {
            StatusText = Loc.F("S_Fb_OpenFailed", ex.Message);
        }
    }
    [ObservableProperty] private bool _showNewsPanel;
    [ObservableProperty] private bool _showIdleHint = true;

    partial void OnShowNewsOnIdleChanged(bool value)
    {
        _settings.ShowNewsOnIdle = value;
        SettingsStore.Save(_settings);
        UpdateIdlePanel();
    }

    private void UpdateIdlePanel()
    {
        var idle = !HasSelectedVersion;
        ShowNewsPanel = idle && ShowNewsOnIdle && News.Count > 0;
        ShowIdleHint = idle && !ShowNewsPanel;
    }

    /// (重新)拉取快讯:源地址 / 代理 / 翻译三项设置变化后调用
    public async Task RefreshNewsAsync()
    {
        News.Clear();
        foreach (var item in await _news.GetLatestAsync(6, NewsSourceUrl, NewsProxy, NewsTranslate))
        {
            // 摘要截到 ~90 字,配合模板两行高度,避免中途硬切半句话
            if (item.Summary.Length > 90) item.Summary = item.Summary[..90].TrimEnd() + "…";
            News.Add(item);
        }
        UpdateIdlePanel();
    }

    // ── 设置 · 版本与更新 ──
    public string VersionText { get; } = AppInfo.VersionText;

    /// GPU 渲染层级:2 = 完整硬件加速,1 = 部分,0 = 软件渲染
    public string RenderInfo { get; } = (System.Windows.Media.RenderCapability.Tier >> 16) switch
    {
        2 => "渲染:GPU 硬件加速(Tier 2)",
        1 => "渲染:部分硬件加速(Tier 1)",
        _ => "渲染:软件渲染(硬件加速不可用)",
    };
    public string BuildDateText { get; } = AppInfo.BuildDate;
    [ObservableProperty] private string _installDate = "";
    /// 「上次游戏」:启动时自动选中上次玩的版本(设置可关)
    [ObservableProperty] private bool _rememberLastVersion = true;
    [ObservableProperty] private string _updateStatus = "";
    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private string _latestVersion = "";
    [ObservableProperty] private UpdateChannelOption? _selectedChannel;

    // ── 设置 · 动画速度 ──

    public ObservableCollection<AnimationSpeedOption> AnimationSpeeds { get; } = new();

    [ObservableProperty] private AnimationSpeedOption? _selectedAnimationSpeed;

    partial void OnSelectedAnimationSpeedChanged(AnimationSpeedOption? value)
    {
        if (value == null) return;
        Motion.Motion.SetSpeed(value.Value);
        _settings.AnimationSpeed = value.Value;
        SettingsStore.Save(_settings);
    }

    public ObservableCollection<UpdateChannelOption> Channels { get; } = new();

    /// 通道/速度这类"预设列表"的名称要随语言重建(XAML 里通过 ItemTemplate 绑定 Name)
    private void RefreshLocalizedLists()
    {
        var channel = SelectedChannel?.Channel ?? UpdateChannel.Release;
        Channels.Clear();
        Channels.Add(new UpdateChannelOption(UpdateChannel.Release, "S_ChRelease"));
        Channels.Add(new UpdateChannelOption(UpdateChannel.Beta, "S_ChBeta"));
        Channels.Add(new UpdateChannelOption(UpdateChannel.Alpha, "S_ChAlpha"));
        Channels.Add(new UpdateChannelOption(UpdateChannel.Dev, "S_ChDev"));
        SelectedChannel = Channels.First(c => c.Channel == channel);

        var speed = SelectedAnimationSpeed?.Value ?? 1.0;
        AnimationSpeeds.Clear();
        AnimationSpeeds.Add(new AnimationSpeedOption("S_AS05", 0.5));
        AnimationSpeeds.Add(new AnimationSpeedOption("S_AS075", 0.75));
        AnimationSpeeds.Add(new AnimationSpeedOption("S_AS1", 1.0));
        AnimationSpeeds.Add(new AnimationSpeedOption("S_AS125", 1.25));
        AnimationSpeeds.Add(new AnimationSpeedOption("S_AS15", 1.5));
        AnimationSpeeds.Add(new AnimationSpeedOption("S_AS2", 2.0));
        SelectedAnimationSpeed = AnimationSpeeds.FirstOrDefault(a => Math.Abs(a.Value - speed) < 0.01) ?? AnimationSpeeds[2];

        ScanJava();        // Java 来源文案("内置/注册表…")是模型计算属性 → 重建列表刷新
        ApplyFilter();     // 版本列表的类型标签同理
        RefreshInstalled();
    }

    partial void OnSelectedChannelChanged(UpdateChannelOption? value)
    {
        if (value == null) return;
        _settings.UpdateChannel = value.Channel.ToString();
        SettingsStore.Save(_settings);
    }

    [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        UpdateStatus = Loc.T("S_Msg_CheckingUpdate");
        var result = await _update.CheckAsync(SelectedChannel?.Channel ?? UpdateChannel.Release, VersionText);
        UpdateStatus = result.Message;
        UpdateAvailable = result.HasUpdate;
        LatestVersion = result.LatestVersion;
    }

    /// 立即更新:下载全量包 → 校验 → 调度重启替换
    [RelayCommand]
    private async Task ApplyUpdateAsync()
    {
        if (!UpdateAvailable) return;
        if (System.Windows.MessageBox.Show(Loc.F("S_Upd_Confirm", LatestVersion), Loc.T("S_ApplyUpdate"),
                System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question)
            != System.Windows.MessageBoxResult.Yes) return;

        try
        {
            IsBusy = true;
            var ps1 = await _update.DownloadAndStageAsync(
                SelectedChannel?.Channel ?? UpdateChannel.Release, LatestVersion,
                p => UpdateStatus = Loc.F("S_Upd_Downloading", p));
            System.Windows.MessageBox.Show(Loc.T("S_Upd_Ready"), Loc.T("S_ApplyUpdate"),
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{ps1}\" -TargetPid {Environment.ProcessId}")
            { CreateNoWindow = true, UseShellExecute = false });
            LogService.Info($"已调度更新到 {LatestVersion},启动器退出中");
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            UpdateStatus = Loc.F("S_Upd_ApplyFailed", ex.Message);
            LogService.Error("应用更新失败", ex);
        }
        finally { IsBusy = false; }
    }

    // ── 设置 · 外观(主题色 / 主页背景图) ──

    public IReadOnlyList<AccentOption> AccentOptions { get; } = new[]
    {
        new AccentOption { Name = "拂晓蓝", Hex = "#5B8CF5", Color = System.Windows.Media.Color.FromRgb(0x5B, 0x8C, 0xF5) },
        new AccentOption { Name = "暮紫", Hex = "#8B5CF6", Color = System.Windows.Media.Color.FromRgb(0x8B, 0x5C, 0xF6) },
        new AccentOption { Name = "青碧", Hex = "#14B8A6", Color = System.Windows.Media.Color.FromRgb(0x14, 0xB8, 0xA6) },
        new AccentOption { Name = "松绿", Hex = "#22C55E", Color = System.Windows.Media.Color.FromRgb(0x22, 0xC5, 0x5E) },
        new AccentOption { Name = "琥珀", Hex = "#F59E0B", Color = System.Windows.Media.Color.FromRgb(0xF5, 0x9E, 0x0B) },
        new AccentOption { Name = "绯红", Hex = "#EF4444", Color = System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44) },
    };

    [ObservableProperty] private string _backgroundImage = "";
    [ObservableProperty] private System.Windows.Media.ImageBrush? _heroBrush;
    /// 无背景图时的 Hero 底色:主题色渐隐到深色(跟随主题色)
    [ObservableProperty] private System.Windows.Media.Brush? _heroGradient;

    [RelayCommand]
    private void SelectAccent(AccentOption option)
    {
        foreach (var item in AccentOptions)
            item.IsSelected = ReferenceEquals(item, option);

        Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(option.Color, Wpf.Ui.Appearance.ApplicationTheme.Dark);
        UpdateHeroGradient(option.Color);
        _settings.AccentColor = option.Hex;
        SettingsStore.Save(_settings);
    }

    private void UpdateHeroGradient(System.Windows.Media.Color accent)
    {
        var gradient = new System.Windows.Media.LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint = new System.Windows.Point(1, 1),
        };
        gradient.GradientStops.Add(new System.Windows.Media.GradientStop(
            System.Windows.Media.Color.FromArgb(0x7A, accent.R, accent.G, accent.B), 0));
        gradient.GradientStops.Add(new System.Windows.Media.GradientStop(
            System.Windows.Media.Color.FromArgb(0x26, accent.R, accent.G, accent.B), 0.5));
        gradient.GradientStops.Add(new System.Windows.Media.GradientStop(
            System.Windows.Media.Color.FromArgb(0xFF, 0x1A, 0x1C, 0x21), 1));
        gradient.Freeze();
        HeroGradient = gradient;
    }

    partial void OnBackgroundImageChanged(string value)
    {
        System.Windows.Media.ImageBrush? brush = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(value) && File.Exists(value))
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(value);
                bmp.EndInit();
                bmp.Freeze();
                brush = new System.Windows.Media.ImageBrush(bmp)
                {
                    Stretch = System.Windows.Media.Stretch.UniformToFill,
                };
                brush.Freeze();
            }
        }
        catch
        {
            StatusText = "这张背景图读不出来(支持 PNG / JPG / BMP / GIF),换一张试试";
        }
        HeroBrush = brush;
    }

    [RelayCommand]
    private void ChooseBackground()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择主页背景图",
            Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
        };
        if (dialog.ShowDialog() != true) return;
        BackgroundImage = dialog.FileName;
        _settings.BackgroundImage = BackgroundImage;
        SettingsStore.Save(_settings);
    }

    [RelayCommand]
    private void ClearBackground()
    {
        BackgroundImage = "";
        _settings.BackgroundImage = "";
        SettingsStore.Save(_settings);
    }

    public async Task LoadAsync()
    {
        IsBusy = true;
        _suppressDirty = true; // 加载期间的赋值不算"未保存改动"
        StatusText = Loc.T("S_Msg_PullingManifest");
        try
        {
            _settings = SettingsStore.Load();
            MaxMemoryMb = _settings.MaxMemoryMb <= 0 ? 4096 : _settings.MaxMemoryMb;
            GameRoot = string.IsNullOrWhiteSpace(_settings.GameRoot) ? DetectDefaultGameRoot() : _settings.GameRoot;
            JvmArgs = _settings.JvmArgs;
            Username = string.IsNullOrWhiteSpace(_settings.Username) ? "Player" : _settings.Username;
            AutoConcurrency = _settings.DownloadConcurrency <= 0; // 0 = 自动
            DownloadConcurrency = Math.Clamp(_settings.DownloadConcurrency <= 0 ? 256 : _settings.DownloadConcurrency, 16, 1024);
            InstallDate = _settings.InstallDate;

            RefreshLocalizedLists(); // 先建好通道/速度预设列表(空列表取 First 会崩)
            SelectedChannel = Channels.FirstOrDefault(c => c.Channel.ToString() == _settings.UpdateChannel) ?? Channels[0];

            BackgroundImage = _settings.BackgroundImage;
            SelectedAnimationSpeed = AnimationSpeeds.FirstOrDefault(a => Math.Abs(a.Value - _settings.AnimationSpeed) < 0.01)
                                     ?? AnimationSpeeds[2];
            var accent = AccentOptions.FirstOrDefault(a => string.Equals(a.Hex, _settings.AccentColor, StringComparison.OrdinalIgnoreCase)) ?? AccentOptions[0];
            foreach (var item in AccentOptions) item.IsSelected = ReferenceEquals(item, accent);
            Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(accent.Color, Wpf.Ui.Appearance.ApplicationTheme.Dark);
            UpdateHeroGradient(accent.Color);

            ScanJava();

            _allVersions = (await _manifest.GetVersionsAsync()).ToList();
            ApplyFilter();

            StatusText = Loc.F("S_Msg_Ready", _allVersions.Count(v => v.IsRelease), Javas.Count);
            RefreshInstalled();

            // 启动器唯一标识 = 构建标识(编译期嵌入二进制) + 安装段(随机,与机器指纹绑定)
            // 配置被整体拷到别的机器 → 指纹不符 → 重生成安装段,保证标识不重复
            var machine = FeedbackService.MachineFingerprint();
            if (string.IsNullOrWhiteSpace(_settings.LauncherId) ||
                !string.Equals(_settings.LauncherMachine, machine, StringComparison.OrdinalIgnoreCase))
            {
                _settings.LauncherId = Guid.NewGuid().ToString("N")[..12];
                _settings.LauncherMachine = machine;
                SettingsStore.Save(_settings);
            }
            LauncherId = $"{AppInfo.DisplayName}-{AppInfo.BuildTag}-{_settings.LauncherId}";
            LauncherIdText = $"{Loc.T("S_LauncherIdPrefix")}: {LauncherId}";
            UseTunaJdkMirror = _settings.UseTunaJdkMirror;
            _ = ProbeTunaMirrorAsync(); // 连通性自动探测(不通过则回退官方源)
            SelectedLanguage = LauncherLanguages.FirstOrDefault(l => l.Code == _settings.LauncherLanguage) ?? LauncherLanguages[0];
            // 未自定义时用官方仓库作为反馈默认目标
            FeedbackRepo = string.IsNullOrWhiteSpace(_settings.FeedbackRepo) ? AppInfo.RepoUrl : _settings.FeedbackRepo;

            // 今日 MC 快讯(官方源;失败静默)
            NewsSourceUrl = _settings.NewsSourceUrl;
            NewsProxy = _settings.NewsProxy;
            NewsTranslate = _settings.NewsTranslate;
            ShowNewsOnIdle = _settings.ShowNewsOnIdle;

            // 「上次游戏」:自动选中上次启动的版本(可在设置里关;版本已被卸载则忽略)
            RememberLastVersion = _settings.RememberLastVersion;
            if (RememberLastVersion && !string.IsNullOrWhiteSpace(_settings.LastPlayedVersion))
            {
                var last = InstalledVersions.FirstOrDefault(v => v.Id == _settings.LastPlayedVersion);
                if (last != null) SelectedVersion = last;
            }

            await RefreshNewsAsync();
        }
        catch (Exception ex)
        {
            StatusText = Loc.T("S_Msg_PullFailed") + ex.Message;
            LogService.Error("拉取版本清单失败", ex);
        }
        finally
        {
            _suppressDirty = false;
            SettingsDirty = false; // 刚加载完=没改过
            IsBusy = false;
        }
    }

    private void ScanJava()
    {
        Javas.Clear();
        foreach (var java in _java.FindInstalled())
            Javas.Add(java);

        // 用户手动添加过的路径(JAVA_HOME/注册表外)
        foreach (var home in _settings.ExtraJavaHomes)
        {
            try
            {
                var exe = Path.Combine(home, "bin", "java.exe");
                if (!File.Exists(exe)) continue;
                if (Javas.Any(j => string.Equals(j.Home, home, StringComparison.OrdinalIgnoreCase))) continue;
                Javas.Add(new JavaRuntime { Home = home, JavaExe = exe, Version = 0, Source = JavaSource.Bundled });
            }
            catch { /* 忽略无效路径 */ }
        }

        SelectedJava = Javas.FirstOrDefault();
    }

    [RelayCommand]
    private void GoTo(string page) => CurrentPage = Enum.TryParse<AppPage>(page, out var p) ? p : AppPage.Home;

    partial void OnFilterChanged(string value) => ApplyFilter();
    partial void OnVersionKindChanged(VersionKindFilter value) => ApplyFilter();

    /// 愚人节版本判定:类型为快照且发布于 4 月 1 日前后(官方历年愚人节版本的规律)
    private static bool IsAprilFools(VersionEntry v)
        => v.Type == "snapshot" && v.ReleaseTime.Month == 4 && v.ReleaseTime.Day <= 2;

    private void ApplyFilter()
    {
        Versions.Clear();
        IEnumerable<VersionEntry> query = _allVersions;
        query = VersionKind switch
        {
            VersionKindFilter.Release => query.Where(v => v.Type == "release"),
            VersionKindFilter.Snapshot => query.Where(v => v.Type == "snapshot" && !IsAprilFools(v)),
            VersionKindFilter.AprilFools => query.Where(IsAprilFools),
            VersionKindFilter.Ancient => query.Where(v => v.Type is "old_alpha" or "old_beta"),
            _ => query,
        };
        if (!string.IsNullOrWhiteSpace(Filter))
            query = query.Where(v => v.Id.Contains(Filter.Trim(), StringComparison.OrdinalIgnoreCase));
        foreach (var version in query.Take(300))
            Versions.Add(version);
    }

    /// 扫描本机已安装版本:游戏目录 versions/<id>/<id>.jar 存在即视为安装
    public void RefreshInstalled()
    {
        var prevId = SelectedVersion?.Id; // Clear 会经 ListView 双向绑定清掉选中项,先记住
        InstalledVersions.Clear();
        try
        {
            var versionsDir = Path.Combine(GameRoot, "versions");
            if (Directory.Exists(versionsDir))
            {
                foreach (var dir in Directory.GetDirectories(versionsDir))
                {
                    var id = Path.GetFileName(dir);
                    if (!File.Exists(Path.Combine(dir, id + ".jar"))) continue;
                    var entry = _allVersions.FirstOrDefault(v => v.Id == id)
                                ?? new VersionEntry { Id = id, Type = "local" };
                    InstalledVersions.Add(entry);
                }
            }
        }
        catch { /* 目录不可读时视为未安装 */ }
        ShowEmptyState = InstalledVersions.Count == 0;

        // 重建后按 Id 恢复选中(版本已被删除则保持未选)
        if (prevId != null && SelectedVersion?.Id != prevId)
            SelectedVersion = InstalledVersions.FirstOrDefault(v => v.Id == prevId);

        // 版本同步维护表:按隔离名单分区(单一数据源 = settings.IsolatedVersions)
        SyncedVersions.Clear();
        IsolatedVersionEntries.Clear();
        foreach (var entry in InstalledVersions)
        {
            if (IsIsolated(entry.Id)) IsolatedVersionEntries.Add(entry);
            else SyncedVersions.Add(entry);
        }
    }

    [RelayCommand]
    private void GoInstall() => CurrentPage = AppPage.Download;

    partial void OnSelectedVersionChanged(VersionEntry? value)
    {
        HasSelectedVersion = value != null;
        OnPropertyChanged(nameof(HasNoSelection));
        OnPropertyChanged(nameof(HeroTitle));
        UpdateIdlePanel();
        if (value != null)
            _ = LoadDetailAsync(value);
    }

    private async Task LoadDetailAsync(VersionEntry entry)
    {
        IsBusy = true;
        StatusText = Loc.F("S_Msg_ReadingVersion", entry.Id);
        try
        {
            var detail = await _manifest.GetDetailAsync(entry);
            SelectedDetail = detail;
            JavaHint = detail.JavaVersion is { MajorVersion: > 0 } hint
                ? Loc.F("S_JavaHintOfficial", hint.MajorVersion) +
                  (hint.Component is { Length: > 0 } component ? $"({component})" : "")
                : Loc.T("S_JavaHintNone");
            RefreshCheck();
            StatusText = Loc.F("S_Msg_Selected", entry.Id);
        }
        catch (Exception ex)
        {
            StatusText = Loc.T("S_Msg_ReadFailed") + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedJavaChanged(JavaRuntime? value) => RefreshCheck();
    partial void OnMaxMemoryMbChanged(int value) => RefreshCheck();
    partial void OnGameRootChanged(string value) => RefreshInstalled();

    private LaunchOptions BuildOptions() => new()
    {
        PlayerName = string.IsNullOrWhiteSpace(Username) ? "Player" : Username.Trim(),
        MaxMemoryMb = Math.Clamp(MaxMemoryMb, 512, 65536),
        ExtraJvmArgs = string.IsNullOrWhiteSpace(JvmArgs) ? null : JvmArgs.Trim(),
    };

    private void RefreshCheck()
    {
        // 缺对应 Java 时给出"下载 Java N"入口(内置运行时,Adoptium)
        var needJava = SelectedDetail?.JavaVersion?.MajorVersion ?? 0;
        NeedsJavaDownload = needJava > 0 && (SelectedJava?.Version ?? 0) < needJava;
        JavaDownloadText = NeedsJavaDownload ? Loc.F("S_DownloadJava", needJava) : "";

        if (SelectedDetail == null || string.IsNullOrWhiteSpace(GameRoot))
        {
            CheckText = SelectedDetail == null && SelectedVersion == null
                ? Loc.T("S_PickVersionHint")
                : "";
            CanLaunch = false;
            LaunchButtonText = Loc.T("S_Launch");
            return;
        }

        var gameDir = EffectiveGameDir(SelectedVersion?.Id ?? SelectedDetail.Id);
        var plan = _launch.BuildPlan(SelectedDetail, SelectedJava, GameRoot, gameDir, BuildOptions());
        CheckText = plan.Missing.Count == 0
            ? Loc.T("S_Msg_ReadyToLaunch")
            : string.Join(Environment.NewLine, plan.Missing);
        CanLaunch = plan.CanLaunch;
        LaunchButtonText = plan.CanLaunch ? Loc.T("S_Launch") : Loc.T("S_LaunchNotReady");
    }

    /// 一键安装 / 补全:客户端 + 库 + natives + 资源对象树 + 日志配置(已存在则跳过)
    [RelayCommand]
    private async Task InstallAsync()
    {
        if (SelectedDetail == null)
        {
            StatusText = Loc.T("S_Msg_PickFirst");
            return;
        }

        IsBusy = true;
        ShowProgress = true;
        Progress = 0;
        var id = SelectedDetail.Id;
        try
        {
            var reporter = new Progress<InstallProgress>(p =>
            {
                Progress = p.Total > 0 ? p.Done * 100.0 / p.Total : 0;
                StatusText = p.Phase switch
                {
                    "资源" => $"正在补齐资源文件 {p.Done}/{p.Total}…",
                    "解压 natives" => $"正在解压原生库 {p.Done}/{p.Total}…",
                    "文件" => $"正在下载 {p.Done}/{p.Total} · {p.Current}",
                    _ => $"{p.Phase}…",
                };
            });
            LogService.Info($"开始安装 {id}(并发 {EffectiveConcurrency})");
            await _install.InstallAsync(SelectedDetail, GameRoot, EffectiveConcurrency, reporter);
            StatusText = Loc.F("S_Msg_InstallDone", id);
            LogService.Info($"{id} 安装完成");
        }
        catch (Exception ex)
        {
            StatusText = Loc.T("S_Msg_InstallFailed") + ex.Message;
            LogService.Error($"安装 {id} 失败", ex);
        }
        finally
        {
            ShowProgress = false;
            IsBusy = false;
            RefreshCheck();
            RefreshInstalled();
        }
    }

    [RelayCommand]
    private async Task LaunchAsync()
    {
        if (SelectedDetail == null || SelectedJava == null) return;

        var gameDir = EffectiveGameDir(SelectedDetail.Id);
        var plan = _launch.BuildPlan(SelectedDetail, SelectedJava, GameRoot, gameDir, BuildOptions());
        if (!plan.CanLaunch)
        {
            StatusText = plan.Summary;
            return;
        }

        StatusText = Loc.T("S_Msg_Launching");
        EnsureGameLanguage(SelectedDetail.Id, gameDir);
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(plan.FileName, plan.Arguments)
            {
                WorkingDirectory = gameDir,
                UseShellExecute = false,
                CreateNoWindow = true, // 隐藏 java.exe 的控制台窗口:只见游戏,不见黑窗
            };
            System.Diagnostics.Process.Start(psi);
            StatusText = Loc.F("S_Msg_Launched", SelectedDetail.Id);
            LogService.Info($"启动 {SelectedDetail.Id}(gameDir={gameDir}):{psi.FileName} {psi.Arguments}");
            _settings.LastPlayedVersion = SelectedDetail.Id; // 记「上次游戏」
            SettingsStore.Save(_settings);
        }
        catch (Exception ex)
        {
            StatusText = Loc.T("S_Msg_LaunchFailed") + ex.Message;
            LogService.Error($"启动 {SelectedDetail.Id} 失败", ex);
        }
        await Task.CompletedTask;
    }

    // ── 设置页命令 ──

    [RelayCommand]
    private void SaveSettings()
    {
        _settings.MaxMemoryMb = Math.Clamp(MaxMemoryMb, 512, 65536);
        _settings.GameRoot = GameRoot;
        _settings.JvmArgs = JvmArgs ?? "";
        _settings.Username = string.IsNullOrWhiteSpace(Username) ? "Player" : Username.Trim();
        _settings.DownloadConcurrency = AutoConcurrency ? 0 : Math.Clamp(DownloadConcurrency, 16, 1024);
        _settings.NewsSourceUrl = NewsSourceUrl.Trim();
        _settings.NewsProxy = NewsProxy.Trim();
        _settings.NewsTranslate = NewsTranslate;
        _settings.FeedbackRepo = FeedbackRepo.Trim();
        _settings.RememberLastVersion = RememberLastVersion;
        _ = RefreshNewsAsync();
        SettingsStore.Save(_settings);
        SettingsDirty = false;
        SettingsSavedHint = Loc.F("S_Msg_Saved", DateTime.Now.ToString("HH:mm:ss"));
        RefreshCheck();
    }

    [RelayCommand]
    private void AddJava()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择 java.exe",
            Filter = "java.exe|java.exe|所有程序|*.exe",
        };
        if (dialog.ShowDialog() != true) return;
        var home = Path.GetDirectoryName(Path.GetDirectoryName(dialog.FileName));
        if (string.IsNullOrWhiteSpace(home)) return;
        if (!_settings.ExtraJavaHomes.Any(h => string.Equals(h, home, StringComparison.OrdinalIgnoreCase)))
        {
            _settings.ExtraJavaHomes.Add(home);
            SettingsStore.Save(_settings);
        }
        ScanJava();
        StatusText = "已添加 Java:" + home;
    }

    /// 游戏设置窗口(语言 / 常用选项 / 键位;模糊搜索;兼容新旧键位格式)
    [RelayCommand]
    private void OpenGameSettings()
    {
        LogService.Info("命令:打开游戏设置");
        try
        {
            var versionId = SelectedVersion?.Id ?? "";
            var window = new Views.GameSettingsWindow(EffectiveGameDir(versionId))
            {
                Owner = System.Windows.Application.Current.MainWindow,
            };
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            StatusText = Loc.T("S_Msg_GameSettingsFailed") + ex.Message;
            LogService.Error("打开游戏设置窗口失败", ex);
        }
    }

    /// 语言自动设置 / 跟随启动器:启动前校验(隔离版本不参与同步)
    private void EnsureGameLanguage(string versionId, string gameDir)
    {
        try
        {
            if (IsIsolated(versionId)) return; // 独立目录,不参与同步

            var file = new GameOptionsFile(Path.Combine(gameDir, "options.txt"));
            var old = file.Get("lang") ?? "";
            var want = _settings.GameLanguageFollows
                ? Loc.MinecraftLangCode   // 跟随启动器
                : old.Length == 0 ? "zh_CN" : null; // 未设置过 → 首次预置简体中文
            if (want == null) return;
            if (old.Length > 0 && old == old.ToLowerInvariant()) want = want.ToLowerInvariant();
            if (old == want) return;

            file.Set("lang", want);
            file.Save();
            LogService.Info($"启动前同步游戏语言:{want}");
        }
        catch { /* 语言同步失败不阻断启动 */ }
    }

    [RelayCommand]
    private void OpenGameFolder()
    {
        try
        {
            var versionId = SelectedVersion?.Id ?? "";
            var dir = EffectiveGameDir(versionId);
            if (string.IsNullOrWhiteSpace(dir)) dir = GameRoot;
            Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start("explorer.exe", dir);
        }
        catch (Exception ex)
        {
            StatusText = Loc.T("S_Msg_FolderFailed") + ex.Message;
        }
    }
}
