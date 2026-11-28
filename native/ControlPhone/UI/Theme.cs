using System.Windows;
using System.Windows.Media;

namespace ControlPhone;

/// <summary>
/// Bảng màu và thước đo của giao diện. Mọi màu đi qua đây (DynamicResource) nên chế độ sáng/tối chỉ là đổi giá trị;
/// bộ tự kiểm tra đo độ tương phản WCAG của từng cặp màu để chữ không bao giờ mờ trên nền.
/// </summary>
public static class Theme
{
    public static string Current { get; private set; } = "dark";

    // bo góc + cỡ chữ dùng chung (XAML đọc bằng {x:Static})
    public const double RCard = 12;
    public const double RCtl = 8;
    public const double RPill = 999;
    public const double FsH1 = 18;
    public const double FsH2 = 15;
    public const double FsBody = 12.5;
    public const double FsCap = 11;

    public static readonly CornerRadius CardRadius = new(RCard);
    public static readonly CornerRadius CtlRadius = new(RCtl);

    /// <summary>Màu (tối, sáng) của từng brush — khoá trùng tên brush trong App.xaml.</summary>
    public static readonly IReadOnlyDictionary<string, (string Dark, string Light)> Palette =
        new Dictionary<string, (string Dark, string Light)>
        {
            ["Bg"] = ("#0B0E14", "#F6F8FC"),
            ["Bg2"] = ("#12161F", "#FFFFFF"),
            ["Bg3"] = ("#1A202C", "#EEF2F9"),
            ["BgElev"] = ("#161C28", "#FFFFFF"),
            ["Line"] = ("#263041", "#DCE3EF"),
            ["Text"] = ("#E8ECF4", "#111827"),
            ["Muted"] = ("#9AA4B8", "#5B6478"),
            ["Accent"] = ("#5B8CFF", "#2450CC"),
            ["AccentDeep"] = ("#3D6BF0", "#2450CC"),
            ["Accent2"] = ("#6D3FE0", "#7C3AED"),
            ["AccentSoft"] = ("#335B8CFF", "#1F2450CC"),
            ["SelBrush"] = ("#5B8CFF", "#2450CC"),
            ["Ok"] = ("#34D399", "#0B7A43"),
            ["Warn"] = ("#FBBF24", "#9A4A08"),
            ["Bad"] = ("#FB7185", "#B3261E"),
            ["OkSoft"] = ("#2634D399", "#1F0B7A43"),
            ["WarnSoft"] = ("#33FBBF24", "#1F9A4A08"),
            ["BadSoft"] = ("#33FB7185", "#1FB3261E"),
            ["Hover"] = ("#1F2634", "#E8EEF9"),
            ["Screen"] = ("#000000", "#000000"),
            ["ToastOk"] = ("#12653F", "#0B7A43"),
            ["ToastErr"] = ("#8C2F2C", "#B3261E"),
            ["ToastInfo"] = ("#222834", "#334155"),
            ["TextOnDark"] = ("#E8ECF4", "#E8ECF4"),
            ["MutedOnDark"] = ("#AAB0C0", "#AAB0C0"),
            ["RingOnDark"] = ("#CCD0DD", "#CCD0DD"),
            ["Scrim"] = ("#B3000000", "#B3000000"),
            ["ScrimSoft"] = ("#80000000", "#80000000"),
        };

    /// <summary>Khoá brush mà XAML/code dùng — thiếu một khoá là chỗ đó giữ màu tối khi đổi sang chế độ sáng.</summary>
    public static readonly string[] Required =
    [
        "Bg", "Bg2", "Bg3", "BgElev", "Line", "Text", "Muted", "Accent", "AccentDeep", "Accent2", "AccentSoft",
        "Ok", "Warn", "Bad", "OkSoft", "WarnSoft", "BadSoft", "Hover", "Screen", "SelBrush", "TextOnDark",
        "MutedOnDark", "RingOnDark", "Scrim", "ScrimSoft", "ToastOk", "ToastErr", "ToastInfo",
    ];

    /// <summary>Màu cố ý giống nhau ở hai chế độ (màn hình điện thoại, lớp phủ trên ảnh) — không phải lỗi.</summary>
    public static readonly IReadOnlySet<string> ThemeInvariant =
        new HashSet<string> { "Screen", "TextOnDark", "MutedOnDark", "RingOnDark", "Scrim", "ScrimSoft" };

    /// <summary>Hai đầu dải màu của nút chính: chữ trắng phải đủ tương phản ở cả hai đầu.</summary>
    public static (string Start, string Stop) AccentStops(string theme) =>
        theme == "light" ? ("#2450CC", "#7C3AED") : ("#3D6BF0", "#6D3FE0");

    public static void Apply(string theme)
    {
        Current = theme == "light" ? "light" : "dark";
        var res = Application.Current.Resources;
        foreach (var (key, (dark, light)) in Palette)
            res[key] = new SolidColorBrush(Parse(Current == "light" ? light : dark));
        var (start, stop) = AccentStops(Current);
        res["AccentGrad"] = new LinearGradientBrush(Parse(start), Parse(stop), new Point(0, 0), new Point(1, 1));
    }

    public static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    /// <summary>Độ tương phản WCAG 2.1 giữa hai màu (1 = trùng nhau, 21 = đen trên trắng).</summary>
    public static double Contrast(string a, string b) => Contrast(Parse(a), Parse(b));

    public static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        var (hi, lo) = la >= lb ? (la, lb) : (lb, la);
        return (hi + 0.05) / (lo + 0.05);
    }

    static double Luminance(Color c)
    {
        static double Ch(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
    }
}

/// <summary>Biểu tượng (font Segoe Fluent Icons / Segoe MDL2 Assets có sẵn trên Windows 10/11).</summary>
public static class Icons
{
    static readonly Dictionary<string, string> Map = new()
    {
        ["sidebar"] = "", ["phone"] = "", ["download"] = "", ["eye"] = "", ["pause"] = "", ["play"] = "",
        ["grid"] = "", ["fit"] = "", ["heart"] = "", ["moon"] = "", ["sun"] = "", ["settings"] = "",
        ["search"] = "", ["checksq"] = "", ["square"] = "", ["invert"] = "", ["back"] = "", ["home"] = "",
        ["recents"] = "", ["power"] = "", ["voldown"] = "", ["volup"] = "", ["unlock"] = "", ["screenoff"] = "",
        ["type"] = "", ["clipboard"] = "", ["apps"] = "", ["package"] = "", ["upload"] = "", ["link"] = "",
        ["terminal"] = "", ["camera"] = "", ["more"] = "", ["left"] = "", ["right"] = "", ["x"] = "",
        ["rotate"] = "", ["bell"] = "", ["shotcopy"] = "", ["folder"] = "", ["info"] = "", ["monitor"] = "",
        ["wifi"] = "", ["wifioff"] = "", ["usb"] = "", ["scan"] = "", ["key"] = "", ["refresh"] = "",
        ["eraser"] = "", ["tag"] = "", ["plus"] = "", ["plug"] = "", ["hash"] = "", ["shield"] = "",
        ["trash"] = "", ["file"] = "", ["check"] = "", ["copy"] = "", ["zap"] = "", ["stop"] = "",
        ["maximize"] = "", ["globe"] = "", ["up"] = "", ["mute"] = "", ["warn"] = "", ["keyboard"] = "", ["chat"] = "", ["robot"] = "", ["clock"] = "\uE823"
    };

    public static string Get(string name) => Map.GetValueOrDefault(name, "");
}
