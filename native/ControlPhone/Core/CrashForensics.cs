using System.Diagnostics;
using System.IO;
using System.Text;

namespace ControlPhone.Core;

/// <summary>
/// Đọc bằng chứng của Windows khi tiến trình chết mà không có ngoại lệ .NET: nhật ký sự kiện (lỗi ứng dụng có tên module
/// gây lỗi, độ lệch lệnh), các tệp kết xuất lỗi (crash dump), và lịch sử phát hiện của Windows Defender. Ứng dụng "tự tắt"
/// thường là một trong ba thứ này — và cả ba đều đọc được mà không cần quyền quản trị.
/// </summary>
public static class CrashForensics
{
    /// <summary>Một lần Windows ghi nhận tiến trình của ta chết.</summary>
    public sealed record Fault(string When, string App, string? Module, string? Code, string? Offset)
    {
        public string Line =>
            $"{When} · {App}{(Module is null ? "" : " · module " + Module)}{(Code is null ? "" : " · code " + Code)}{(Offset is null ? "" : " · offset " + Offset)}";
    }

    /// <summary>
    /// Tiến trình đáng quan tâm: chính ứng dụng, và hai công cụ nó tự giải nén ra thư mục tạm — trình diệt virus cách ly
    /// adb.exe/scrcpy-server cũng làm ứng dụng "không kết nối được máy nào" hoặc chết ngay khi mở.
    /// </summary>
    public static bool Relevant(string text) =>
        text.Contains("controlphone", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("scrcpy-server", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("adb.exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>Hàm thuần: lấy các khối sự kiện nhắc tới ControlPhone và rút ra trường hữu ích.</summary>
    public static List<Fault> ParseEventLog(string? text)
    {
        var faults = new List<Fault>();
        if (string.IsNullOrWhiteSpace(text)) return faults;
        foreach (var block in text.Split("\r\n\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Relevant(block)) continue;
            var when = Field(block, "Date") ?? Field(block, "TimeCreated") ?? "?";
            var app = Field(block, "Faulting application name") ?? Field(block, "Application") ?? "ControlPhone";
            var module = Field(block, "Faulting module name");
            var code = Field(block, "Exception code");
            var offset = Field(block, "Fault offset");
            faults.Add(new Fault(when.Trim(), app.Trim(), module?.Trim(), code?.Trim(), offset?.Trim()));
        }
        return faults;
    }

    static string? Field(string block, string name)
    {
        foreach (var raw in block.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith(name, StringComparison.OrdinalIgnoreCase)) continue;
            var i = line.IndexOf(':');
            if (i < 0) continue;
            var v = line[(i + 1)..].Trim();
            if (v.Length > 0) return v;
        }
        return null;
    }

    /// <summary>Hàm thuần: dòng lịch sử Defender nào nhắc tới ứng dụng hoặc công cụ của nó.</summary>
    public static List<string> ParseDefender(string? text)
    {
        var hits = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return hits;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (!Relevant(line)) continue;
            hits.Add(line.Length > 300 ? line[..300] + "…" : line);
        }
        return hits;
    }

    /// <summary>Các tệp kết xuất lỗi của ControlPhone (Windows ghi khi tiến trình chết).</summary>
    public static List<string> DumpFiles()
    {
        var list = new List<string>();
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps");
            if (!Directory.Exists(dir)) return list;
            foreach (var f in Directory.EnumerateFiles(dir, "*.dmp").Where(f => Path.GetFileName(f).Contains("ControlPhone", StringComparison.OrdinalIgnoreCase)))
            {
                var fi = new FileInfo(f);
                list.Add($"{fi.Name} · {fi.Length / 1024} KB · {fi.LastWriteTime:yyyy-MM-dd HH:mm}");
            }
        }
        catch { }
        return list;
    }

    /// <summary>Chạy lệnh ngắn có giới hạn thời gian; lỗi thì trả null (chẩn đoán không được phép làm hỏng chính nó).</summary>
    static string? Run(string exe, string args, int timeoutMs = 4000)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi);
            if (p == null) return null;
            var text = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } return null; }
            return text;
        }
        catch { return null; }
    }

    public static (List<Fault> Faults, List<string> Detections, List<string> Dumps) Collect()
    {
        // 1000 = Application Error (module gây lỗi), 1026 = .NET Runtime (ngoại lệ không xử lý)
        var events = Run("wevtutil", "qe Application \"/q:*[System[(EventID=1000 or EventID=1026)]]\" /c:40 /rd:true /f:text");
        var defender = Run("powershell", "-NoProfile -NonInteractive -Command \"Get-MpThreatDetection | Sort-Object InitialDetectionTime -Descending | Select-Object -First 5 | ForEach-Object { $_.InitialDetectionTime.ToString('s') + ' | ' + ($_.Resources -join ';') }\"");
        return (ParseEventLog(events), ParseDefender(defender), DumpFiles());
    }

    public static string Report((List<Fault> Faults, List<string> Detections, List<string> Dumps) data)
    {
        var sb = new StringBuilder();
        sb.AppendLine(I18n.T("Bản ghi lỗi của Windows"));
        sb.AppendLine(I18n.T("Số lần Windows ghi nhận ControlPhone bị lỗi") + ": " + data.Faults.Count);
        foreach (var f in data.Faults.Take(5)) sb.AppendLine("  " + f.Line);
        sb.AppendLine(I18n.T("Tệp kết xuất lỗi của Windows") + ": " + data.Dumps.Count);
        foreach (var d in data.Dumps.Take(5)) sb.AppendLine("  " + d);
        sb.AppendLine(I18n.T("Lần Windows Defender phát hiện ControlPhone") + ": " + data.Detections.Count);
        foreach (var d in data.Detections.Take(5)) sb.AppendLine("  " + d);
        return sb.ToString().TrimEnd();
    }
}
