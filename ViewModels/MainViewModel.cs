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
    private readonly ManifestService _manifest = new();
    private readonly JavaService _java = new();
    private readonly LaunchService _launch = new();
    private readonly DownloadService _download = new();
    private readonly InstallService _install = new();
    private readonly UpdateService _update = new();
    private readonly NewsService _news = new();
    private LauncherSettings _settings = new();

    private List<VersionEntry> _allVersions = new();

    public ObservableCollection<VersionEntry> Versions { get; } = new();

    /// 主页列表:仅本机已安装(versions/<id>/<id>.jar 存在)的版本
    public ObservableCollection<VersionEntry> InstalledVersions { get; } = new();

    public ObservableCollection<JavaRuntime> Javas { get; } = new();

    // ── 导航 ──
    [ObservableProperty] private AppPage _currentPage = AppPage.Home;
    [ObservableProperty] private string _pageTitle = "主页";

    partial void OnCurrentPageChanged(AppPage value)
    {
        PageTitle = value switch
        {
            AppPage.Home => "主页",
            AppPage.Download => "下载",
            AppPage.Settings => "设置",
            _ => "关于",
        };
        if (value == AppPage.Home)
            RefreshInstalled(); // 回主页时刷新本机已安装列表
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
    /// 手动模式下滑杆才可用
    public bool ManualConcurrencyEnabled => !AutoConcurrency;

    partial void OnAutoConcurrencyChanged(bool value)
    {
        OnPropertyChanged(nameof(EffectiveConcurrency));
        OnPropertyChanged(nameof(ManualConcurrencyEnabled));
        _settings.DownloadConcurrency = value ? 0 : Math.Clamp(DownloadConcurrency, 16, 1024);
        SettingsStore.Save(_settings);
    }

    partial void OnDownloadConcurrencyChanged(int value)
    {
        if (AutoConcurrency) return;
        OnPropertyChanged(nameof(EffectiveConcurrency));
        _settings.DownloadConcurrency = Math.Clamp(value, 16, 1024);
        SettingsStore.Save(_settings);
    }

    // ── 主页空状态 ──
    [ObservableProperty] private bool _showEmptyState;

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

    // ── 关于 · 帮助与反馈 ──

    [RelayCommand]
    private void ReportBug() => OpenFeedback(FeedbackKind.Bug);

    [RelayCommand]
    private void SuggestIdea() => OpenFeedback(FeedbackKind.Idea);

    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(LogService.LogDir);
            System.Diagnostics.Process.Start("explorer.exe", LogService.LogDir);
        }
        catch (Exception ex) { StatusText = "打开日志目录失败:" + ex.Message; }
    }

    private void OpenFeedback(FeedbackKind kind)
    {
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
            var url = FeedbackService.BuildIssueUrl(repo, kind, _settings.LauncherId, javaText);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            LogService.Info($"打开反馈页({kind})");
            StatusText = "已在浏览器打开 GitHub 反馈页(环境信息与日志已自动填充,提交前可编辑)";
        }
        catch (Exception ex)
        {
            StatusText = "打开浏览器失败:" + ex.Message;
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
            News.Add(item);
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
    [ObservableProperty] private string _updateStatus = "";
    [ObservableProperty] private UpdateChannelOption? _selectedChannel;

    // ── 设置 · 动画速度 ──

    public IReadOnlyList<AnimationSpeedOption> AnimationSpeeds { get; } = new[]
    {
        new AnimationSpeedOption("0.5× 舒缓", 0.5),
        new AnimationSpeedOption("0.75× 偏慢", 0.75),
        new AnimationSpeedOption("1× 标准", 1.0),
        new AnimationSpeedOption("1.25× 偏快", 1.25),
        new AnimationSpeedOption("1.5× 轻快", 1.5),
        new AnimationSpeedOption("2× 迅捷", 2.0),
    };

    [ObservableProperty] private AnimationSpeedOption? _selectedAnimationSpeed;

    partial void OnSelectedAnimationSpeedChanged(AnimationSpeedOption? value)
    {
        if (value == null) return;
        Motion.Motion.SetSpeed(value.Value);
        _settings.AnimationSpeed = value.Value;
        SettingsStore.Save(_settings);
    }

    public IReadOnlyList<UpdateChannelOption> Channels { get; } = new[]
    {
        new UpdateChannelOption(UpdateChannel.Release, "正式版"),
        new UpdateChannelOption(UpdateChannel.Beta, "Beta 内测"),
        new UpdateChannelOption(UpdateChannel.Alpha, "Alpha 内测"),
        new UpdateChannelOption(UpdateChannel.Dev, "Dev 内测"),
    };

    partial void OnSelectedChannelChanged(UpdateChannelOption? value)
    {
        if (value == null) return;
        _settings.UpdateChannel = value.Channel.ToString();
        SettingsStore.Save(_settings);
    }

    [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        UpdateStatus = "正在检查更新…";
        UpdateStatus = await _update.CheckAsync(SelectedChannel?.Channel ?? UpdateChannel.Release, VersionText);
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
        StatusText = "正在拉取 Mojang 版本清单…";
        try
        {
            _settings = SettingsStore.Load();
            MaxMemoryMb = _settings.MaxMemoryMb <= 0 ? 4096 : _settings.MaxMemoryMb;
            GameRoot = string.IsNullOrWhiteSpace(_settings.GameRoot) ? SettingsStore.DefaultGameRoot : _settings.GameRoot;
            JvmArgs = _settings.JvmArgs;
            Username = string.IsNullOrWhiteSpace(_settings.Username) ? "Player" : _settings.Username;
            AutoConcurrency = _settings.DownloadConcurrency <= 0; // 0 = 自动
            DownloadConcurrency = Math.Clamp(_settings.DownloadConcurrency <= 0 ? 256 : _settings.DownloadConcurrency, 16, 1024);
            InstallDate = _settings.InstallDate;
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

            StatusText = $"就绪 · {_allVersions.Count(v => v.IsRelease)} 个正式版 · 本机 {Javas.Count} 个 Java";
            RefreshInstalled();

            // 启动器唯一标识:首次运行生成
            if (string.IsNullOrWhiteSpace(_settings.LauncherId))
            {
                _settings.LauncherId = Guid.NewGuid().ToString("N")[..16];
                SettingsStore.Save(_settings);
            }
            LauncherIdText = $"启动器标识:{_settings.LauncherId}(反馈时自动附上)";
            // 未自定义时用官方仓库作为反馈默认目标
            FeedbackRepo = string.IsNullOrWhiteSpace(_settings.FeedbackRepo) ? AppInfo.RepoUrl : _settings.FeedbackRepo;

            // 今日 MC 快讯(官方源;失败静默)
            NewsSourceUrl = _settings.NewsSourceUrl;
            NewsProxy = _settings.NewsProxy;
            NewsTranslate = _settings.NewsTranslate;
            ShowNewsOnIdle = _settings.ShowNewsOnIdle;
            await RefreshNewsAsync();
        }
        catch (Exception ex)
        {
            StatusText = "拉取失败:" + ex.Message;
            LogService.Error("拉取版本清单失败", ex);
        }
        finally
        {
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
    }

    [RelayCommand]
    private void GoInstall() => CurrentPage = AppPage.Download;

    partial void OnSelectedVersionChanged(VersionEntry? value)
    {
        HasSelectedVersion = value != null;
        OnPropertyChanged(nameof(HasNoSelection));
        UpdateIdlePanel();
        if (value != null)
            _ = LoadDetailAsync(value);
    }

    private async Task LoadDetailAsync(VersionEntry entry)
    {
        IsBusy = true;
        StatusText = $"正在读取 {entry.Id} 的版本信息…";
        try
        {
            var detail = await _manifest.GetDetailAsync(entry);
            SelectedDetail = detail;
            JavaHint = detail.JavaVersion is { MajorVersion: > 0 } hint
                ? $"官方映射:需要 Java {hint.MajorVersion}" +
                  (hint.Component is { Length: > 0 } component ? $"({component})" : "")
                : "无 Java 版本约束";
            RefreshCheck();
            StatusText = $"已选中 {entry.Id}";
        }
        catch (Exception ex)
        {
            StatusText = "读取失败:" + ex.Message;
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
        if (SelectedDetail == null || string.IsNullOrWhiteSpace(GameRoot))
        {
            CheckText = SelectedDetail == null && SelectedVersion == null
                ? "选择版本查看详情"
                : "";
            CanLaunch = false;
            LaunchButtonText = "开始游戏";
            return;
        }

        var plan = _launch.BuildPlan(SelectedDetail, SelectedJava, GameRoot, BuildOptions());
        CheckText = plan.Missing.Count == 0
            ? "资源就绪,可以出发 ✓"
            : string.Join(Environment.NewLine, plan.Missing);
        CanLaunch = plan.CanLaunch;
        LaunchButtonText = plan.CanLaunch ? "开始游戏" : "还差一点";
    }

    /// 一键安装 / 补全:客户端 + 库 + natives + 资源对象树 + 日志配置(已存在则跳过)
    [RelayCommand]
    private async Task InstallAsync()
    {
        if (SelectedDetail == null)
        {
            StatusText = "先在列表里选一个版本";
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
            StatusText = $"{id} 安装完成 ✓";
            LogService.Info($"{id} 安装完成");
        }
        catch (Exception ex)
        {
            StatusText = "安装失败:" + ex.Message;
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

        var plan = _launch.BuildPlan(SelectedDetail, SelectedJava, GameRoot, BuildOptions());
        if (!plan.CanLaunch)
        {
            StatusText = plan.Summary;
            return;
        }

        StatusText = "启动中…";
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(plan.FileName, plan.Arguments)
            {
                WorkingDirectory = GameRoot,
                UseShellExecute = false,
            };
            System.Diagnostics.Process.Start(psi);
            StatusText = $"已拉起 Java 进程({SelectedDetail.Id})";
            LogService.Info($"启动 {SelectedDetail.Id}:{psi.FileName} {psi.Arguments}");
        }
        catch (Exception ex)
        {
            StatusText = "启动失败:" + ex.Message;
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
        _ = RefreshNewsAsync();
        SettingsStore.Save(_settings);
        SettingsSavedHint = $"已保存 ✓({DateTime.Now:HH:mm:ss})";
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

    [RelayCommand]
    private void OpenGameFolder()
    {
        try
        {
            Directory.CreateDirectory(GameRoot);
            System.Diagnostics.Process.Start("explorer.exe", GameRoot);
        }
        catch (Exception ex)
        {
            StatusText = "打开目录失败:" + ex.Message;
        }
    }
}
