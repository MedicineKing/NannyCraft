using System.Windows;
using System.Windows.Media.Animation;

namespace McLauncher.Motion;

/// 动效规范层(严格落地 Apple HIG + HarmonyOS 动效标准)。
/// - 时长三档:150ms(微交互)/ 220ms(标准·状态变化)/ 350ms(大位移·页面转场)
/// - 缓动:标准曲线 cubic-bezier(0.2, 0, 0, 1);退出 cubic-bezier(0.4, 0, 1, 1)
/// - 尊重系统"动画效果"开关(SystemParameters.ClientAreaAnimation):关闭时所有时长归零
/// - 本项目中所有动画必须经过本类取值,禁止在 XAML 里硬编码时长/曲线
public static class Motion
{
    /// 系统是否要求减少动画(Windows 设置 > 辅助功能 > 显示动画)
    public static bool ReduceMotion => !SystemParameters.ClientAreaAnimation;

    /// 全局动画速度倍率(借鉴 PCL 的"动画速度"思路,非照抄):0.5× 舒缓 … 2× 迅捷,1 = 标准。
    /// 启动时由 App 从设置注入;XAML 里的时长在解析期取到该倍率。
    public static double Speed { get; private set; } = 1.0;

    public static void SetSpeed(double speed) => Speed = Math.Clamp(speed, 0.25, 3.0);

    public static readonly Duration Fast = new(TimeSpan.FromMilliseconds(150));
    public static readonly Duration Normal = new(TimeSpan.FromMilliseconds(220));
    public static readonly Duration Slow = new(TimeSpan.FromMilliseconds(350));

    /// 标准曲线 cubic-bezier(0.2, 0, 0, 1)(进入/强调用,ease-out 系)
    public static IEasingFunction Standard() => new CubicBezierEase(0.2, 0, 0, 1) { EasingMode = EasingMode.EaseOut };

    /// 退出曲线 cubic-bezier(0.4, 0, 1, 1)
    public static IEasingFunction Exit() => new CubicBezierEase(0.4, 0, 1, 1) { EasingMode = EasingMode.EaseOut };

    /// 统一取时长(带减少动画降级 + 速度倍率)
    public static Duration DurationFor(Duration desired)
    {
        if (ReduceMotion) return new Duration(TimeSpan.Zero);
        if (Math.Abs(Speed - 1.0) < 0.001) return desired;
        return new Duration(TimeSpan.FromMilliseconds(desired.TimeSpan.TotalMilliseconds / Speed));
    }

    /// XAML 专用({x:Static m:Motion.FastDuration}):解析时即完成"减少动画"降级,无需运行期替换资源
    public static Duration FastDuration => DurationFor(Fast);
    public static Duration NormalDuration => DurationFor(Normal);
    public static Duration SlowDuration => DurationFor(Slow);

    /// XAML 里可引用的共享缓动实例:{x:Static m:Motion.StandardEase};已冻结,可跨动画安全复用
    public static readonly CubicBezierEase StandardEase = MakeFrozen(0.2, 0, 0, 1);
    public static readonly CubicBezierEase ExitEase = MakeFrozen(0.4, 0, 1, 1);

    private static CubicBezierEase MakeFrozen(double x1, double y1, double x2, double y2)
    {
        var ease = new CubicBezierEase(x1, y1, x2, y2) { EasingMode = EasingMode.EaseOut };
        ease.Freeze();
        return ease;
    }
}

/// WPF 自带緩动没有 cubic-bezier(x1,y1,x2,y2) 工厂,这里用三次贝塞尔求值实现。
/// 公式与 CSS cubic-bezier 一致:x(s) 用牛顿迭代反解 s,再取 y(s)。
public sealed class CubicBezierEase : EasingFunctionBase
{
    private readonly double _x1, _y1, _x2, _y2;

    public CubicBezierEase(double x1, double y1, double x2, double y2)
    {
        _x1 = x1; _y1 = y1; _x2 = x2; _y2 = y2;
    }

    protected override Freezable CreateInstanceCore() => new CubicBezierEase(_x1, _y1, _x2, _y2);

    protected override double EaseInCore(double normalizedTime)
    {
        double s = normalizedTime;
        for (int i = 0; i < 8; i++)
        {
            double x = _Bezier(s, _x1, _x2) - normalizedTime;
            if (Math.Abs(x) < 1e-6) break;
            double dx = _BezierDerivative(s, _x1, _x2);
            if (Math.Abs(dx) < 1e-9) break;
            s = Math.Clamp(s - x / dx, 0, 1);
        }
        return _Bezier(s, _y1, _y2);
    }

    private static double _Bezier(double s, double p1, double p2)
        => 3 * (1 - s) * (1 - s) * s * p1 + 3 * (1 - s) * s * s * p2 + s * s * s;

    private static double _BezierDerivative(double s, double p1, double p2)
        => 3 * (1 - s) * (1 - s) * p1 + 6 * (1 - s) * s * (p2 - p1) + 3 * s * s * (1 - p2);
}
