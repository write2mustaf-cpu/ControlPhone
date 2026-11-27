using System.IO;

namespace ControlPhone.Core;

/// <summary>
/// Giá trị hợp lệ hoá cho cài đặt đọc từ đĩa hoặc nhập từ tệp: một config.json bị sửa tay, một bản cũ hoặc một tệp
/// xuất hỏng không được làm ứng dụng chạy sai (fps = 0, kích thước 0, đường dẫn lạ, ngôn ngữ không tồn tại…).
/// Hàm thuần, không tác dụng phụ, để bản tự kiểm tra gọi trực tiếp.
/// </summary>
public static class SettingsSanitizer
{
    public static readonly string[] Sorts = ["num", "name", "status", "battery"];
    public static readonly string[] Filters = ["all", "usb", "wifi", "bad", "paused", "sel"];
    public static readonly string[] Themes = ["dark", "light"];

    /// <summary>Trả về danh sách mô tả các giá trị đã sửa (rỗng = tệp hợp lệ).</summary>
    public static List<string> Apply(Settings s)
    {
        var fixedUp = new List<string>();
        void Range(string name, int value, int min, int max, Action<int> set)
        {
            var v = Math.Clamp(value, min, max);
            if (v != value) { set(v); fixedUp.Add($"{name}={value}→{v}"); }
        }

        Range(nameof(s.ThumbMaxSize), s.ThumbMaxSize, 160, 1920, v => s.ThumbMaxSize = v);
        Range(nameof(s.ThumbBitRate), s.ThumbBitRate, 30_000, 20_000_000, v => s.ThumbBitRate = v);
        Range(nameof(s.ThumbFps), s.ThumbFps, 1, 60, v => s.ThumbFps = v);
        Range(nameof(s.HqMaxSize), s.HqMaxSize, 320, 2560, v => s.HqMaxSize = v);
        Range(nameof(s.HqBitRate), s.HqBitRate, 500_000, 40_000_000, v => s.HqBitRate = v);
        Range(nameof(s.HqFps), s.HqFps, 5, 60, v => s.HqFps = v);
        Range(nameof(s.MaxParallelStart), s.MaxParallelStart, 1, 30, v => s.MaxParallelStart = v);
        Range(nameof(s.MaxParallelJobs), s.MaxParallelJobs, 1, 50, v => s.MaxParallelJobs = v);

        if (s.PreferTransport is not ("usb" or "wifi"))
        {
            fixedUp.Add($"PreferTransport={s.PreferTransport}→usb");
            s.PreferTransport = "usb";
        }
        var ports = NormalizePorts(s.LanPorts);
        if (ports != s.LanPorts) { fixedUp.Add($"LanPorts={s.LanPorts}→{ports}"); s.LanPorts = ports; }
        if (string.IsNullOrWhiteSpace(s.ScreenshotDir) || HasInvalidPathChars(s.ScreenshotDir))
        {
            fixedUp.Add("ScreenshotDir→default");
            s.ScreenshotDir = Path.Combine(Path.GetDirectoryName(Store.DataDir) ?? AppContext.BaseDirectory, "screenshots");
        }
        return fixedUp;
    }

    public static List<string> Apply(UiPrefs ui)
    {
        var fixedUp = new List<string>();
        if (double.IsNaN(ui.TileW) || double.IsInfinity(ui.TileW) || ui.TileW < 110 || ui.TileW > 520)
        {
            fixedUp.Add($"TileW={ui.TileW}→180");
            ui.TileW = 180;
        }
        if (!Sorts.Contains(ui.Sort)) { fixedUp.Add($"Sort={ui.Sort}→num"); ui.Sort = "num"; }
        if (!IsValidFilter(ui.Filter)) { fixedUp.Add($"Filter={ui.Filter}→all"); ui.Filter = "all"; }
        if (!Themes.Contains(ui.Theme)) { fixedUp.Add($"Theme={ui.Theme}→dark"); ui.Theme = "dark"; }
        // ngôn ngữ lạ (bản cũ / gõ tay) → để trống = dùng ngôn ngữ hệ thống
        if (!string.IsNullOrEmpty(ui.Lang) && !I18n.Langs.Any(l => l.Code == ui.Lang)) { fixedUp.Add($"Lang={ui.Lang}→auto"); ui.Lang = ""; }
        if (ui.ShellHist == null) ui.ShellHist = [];
        else if (ui.ShellHist.Count > 50) { ui.ShellHist = ui.ShellHist.Take(50).ToList(); fixedUp.Add("ShellHist trimmed"); }
        if (ui.Selected == null) ui.Selected = [];
        return fixedUp;
    }

    public static List<string> Apply(Ai.AiSettings a)
    {
        var fixedUp = new List<string>();
        int Clamp(string name, int value, int min, int max, Action<int> set)
        {
            var v = Math.Clamp(value, min, max);
            if (v != value) { set(v); fixedUp.Add($"{name}={value}→{v}"); }
            return v;
        }
        Clamp(nameof(a.MaxToolRounds), a.MaxToolRounds, 1, 20, v => a.MaxToolRounds = v);
        a.ExtraSystemPrompt ??= "";
        if (a.ExtraSystemPrompt.Length > 4000) { a.ExtraSystemPrompt = a.ExtraSystemPrompt[..4000]; fixedUp.Add("ExtraSystemPrompt trimmed"); }
        return fixedUp;
    }

    /// <summary>Áp dụng cho toàn bộ cài đặt đang nạp; ghi lại những gì đã sửa vào nhật ký hoạt động.</summary>
    public static List<string> ApplyAll()
    {
        var fixedUp = new List<string>();
        fixedUp.AddRange(Apply(Store.Settings));
        fixedUp.AddRange(Apply(Store.Ui));
        try { fixedUp.AddRange(Apply(Ai.AiStore.Settings)); } catch { }
        if (fixedUp.Count > 0) OpLog.Add("Đã sửa cài đặt không hợp lệ: " + string.Join(", ", fixedUp), error: false);
        return fixedUp;
    }

    public static bool IsValidFilter(string? f) =>
        !string.IsNullOrEmpty(f) && (Filters.Contains(f) || (f.StartsWith("group:") && f.Length > 6));

    /// <summary>"5555, 15555 abc" → "5555,15555"; bỏ cổng ngoài 1–65535; rỗng → "5555".</summary>
    public static string NormalizePorts(string? raw)
    {
        var ports = (raw ?? "")
            .Split([',', ';', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(p => int.TryParse(p.Trim(), out var n) && n is >= 1 and <= 65535 ? n : 0)
            .Where(n => n > 0)
            .Distinct()
            .ToList();
        return ports.Count == 0 ? "5555" : string.Join(",", ports);
    }

    static bool HasInvalidPathChars(string path)
    {
        try { return path.IndexOfAny(Path.GetInvalidPathChars()) >= 0; } catch { return true; }
    }
}
