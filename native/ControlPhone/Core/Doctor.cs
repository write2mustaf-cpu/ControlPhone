using System.IO;
using System.Text;

namespace ControlPhone.Core;

/// <summary>Sự thật về máy đang chạy — đủ để biết vì sao ứng dụng không mở được, không cần điện thoại.</summary>
public sealed record DoctorFacts(
    int Cores, long RamMb, bool DataWritable, bool FfmpegPresent,
    string DataDir, string AppDir, string Os, string? ConfigError, string DotNet);

/// <summary>
/// "ControlPhone.exe --doctor": in ra một bản chẩn đoán ngắn (phiên bản, Windows, .NET, thư mục dữ liệu có ghi được không,
/// FFmpeg có mặt không, các mốc khởi động và lỗi gần nhất). Mục đích: khi ứng dụng "tự tắt", người dùng có một tệp để gửi
/// thay vì phải đoán — và tệp đó chỉ chứa thông tin kỹ thuật của máy, không có khoá API, không có tên điện thoại.
/// </summary>
public static class Doctor
{
    public static DoctorFacts Collect()
    {
        var cores = Environment.ProcessorCount;
        var ram = 0L;
        try { ram = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024); } catch { }
        var data = Store.DataDir;
        var writable = false;
        try
        {
            Directory.CreateDirectory(data);
            var probe = Path.Combine(data, ".doctor-probe");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            writable = true;
        }
        catch { }
        var ffmpeg = false;
        try { ffmpeg = ControlPhone.Video.Ffmpeg.avcodec_version() > 0; } catch { }
        return new DoctorFacts(cores, ram, writable, ffmpeg, data, AppContext.BaseDirectory,
            Environment.OSVersion.VersionString, Store.LoadError, Environment.Version.ToString());
    }

    public static string Report(DoctorFacts f)
    {
        var sb = new StringBuilder();
        sb.AppendLine(I18n.T("ControlPhone {0} — chẩn đoán khởi động", typeof(Doctor).Assembly.GetName().Version?.ToString(3) ?? "?"));
        sb.AppendLine(I18n.T("Hệ điều hành {0} · .NET {1} · {2} lõi · {3} MB RAM", f.Os, f.DotNet, f.Cores, f.RamMb));
        sb.AppendLine(I18n.T("Thư mục chương trình: {0}", f.AppDir));
        sb.AppendLine(I18n.T("Thư mục dữ liệu: {0}", f.DataDir) + (f.DataWritable ? "" : " " + I18n.T("(KHÔNG ghi được)")));
        sb.AppendLine(I18n.T("FFmpeg: {0}", f.FfmpegPresent ? "ok" : I18n.T("KHÔNG nạp được")));
        if (!string.IsNullOrEmpty(f.ConfigError)) sb.AppendLine(I18n.T("Cấu hình lỗi: {0}", f.ConfigError));
        sb.AppendLine(I18n.T("Tệp lỗi và nhật ký: {0}", Crash.LogPath));
        sb.AppendLine(I18n.T("Số lỗi đã ghi: {0}", CountCrashes()));
        sb.AppendLine(CrashForensics.Report(CrashForensics.Collect()));
        sb.AppendLine(I18n.T("Các mốc khởi động gần nhất:"));
        foreach (var m in StartupTrace.Tail(12)) sb.AppendLine("  " + m);
        return sb.ToString().TrimEnd();
    }

    /// <summary>Vấn đề chặn khởi động (dùng cho bản tự kiểm tra): không ghi được dữ liệu, không nạp được FFmpeg, config lỗi.</summary>
    public static bool LooksUnhealthy(DoctorFacts f) =>
        !f.DataWritable || !f.FfmpegPresent || !string.IsNullOrEmpty(f.ConfigError) || f.Cores <= 0;

    static int CountCrashes()
    {
        try
        {
            if (!File.Exists(Crash.LogPath)) return 0;
            return File.ReadAllLines(Crash.LogPath).Count(l => l.StartsWith('['));
        }
        catch { return 0; }
    }

    /// <summary>Ghi bản chẩn đoán ra data\logs\doctor.txt (và trả về đường dẫn).</summary>
    public static string Write(string? path = null)
    {
        path ??= Path.Combine(Store.DataDir, "logs", "doctor.txt");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, Report(Collect()));
        }
        catch { }
        return path;
    }
}
