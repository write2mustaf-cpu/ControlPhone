using System.IO;
using System.IO.Compression;
using System.Text;
using Ai = ControlPhone.Ai;

namespace ControlPhone.Core;

/// <summary>
/// Gói chẩn đoán để gửi kèm khi báo lỗi: phiên bản, danh sách máy (không có số sê-ri), nhật ký hoạt động, crash.log và
/// cài đặt. Không bao giờ chứa khoá API (ai.json bị loại hoàn toàn) và mọi nội dung văn bản được che khoá một lần nữa
/// trước khi ghi — tệp này rời khỏi máy người dùng nên phải an toàn khi đọc bởi người khác.
/// </summary>
public static class DiagBundle
{
    public static string DefaultName() => $"ControlPhone-diag-{DateTime.Now:yyyyMMdd-HHmmss}.zip";

    public static string[] Entries(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        return zip.Entries.Select(e => e.FullName).OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Tạo gói; <paramref name="deviceLines"/> là mô tả máy do tầng giao diện cung cấp (không có sê-ri).</summary>
    public static string Build(string zipPath, IEnumerable<string>? deviceLines = null, int opLines = 200)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(zipPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        if (File.Exists(zipPath)) File.Delete(zipPath);

        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        Put(zip, "README.txt", Readme());
        Put(zip, "versions.txt", Versions());
        Put(zip, "devices.txt", Devices(deviceLines));
        Put(zip, "oplog.txt", OpLog.Format(Math.Clamp(opLines, 1, 600)));
        Copy(zip, "logs/crash.log", Crash.LogPath);
        Copy(zip, "logs/crash.log.1", Crash.LogPath + ".1");
        Copy(zip, "settings/config.json", Path.Combine(Store.DataDir, "config.json"));
        Copy(zip, "settings/ui.json", Path.Combine(Store.DataDir, "ui.json"));
        return zipPath;
    }

    /// <summary>Che khoá API/link có token trong văn bản tự do (crash.log cũ, lệnh ADB, log nhà cung cấp).</summary>
    public static string Scrub(string? text) => Ai.AiSecret.Redact(text, null);

    static string Readme() =>
        """
        ControlPhone — diagnostics bundle
        =================================

        What is inside
          versions.txt        app, Windows, .NET, ADB/scrcpy/FFmpeg versions, CPU count
          devices.txt         phone number, label, transport, Android version — NO serial numbers
          oplog.txt           last operations from this session (connections, batches, errors)
          logs/crash.log      unhandled exceptions (rotated, capped at 512 KB each)
          settings/*.json     config.json and ui.json as they are on disk

        What is NOT inside
          - API keys and tokens: data/ai/ai.json is excluded entirely, and every text file is
            scrubbed again before it is written (sk-…, AIza…, Bearer …, ?key=… are masked).
          - Phone serial numbers, clipboard contents, screenshots, file names from your PC.

        Send this file to mwings740@gmail.com together with what you were doing when it failed.
        """;

    static string Versions()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"ControlPhone   {typeof(DiagBundle).Assembly.GetName().Version}");
        sb.AppendLine($"Windows        {Environment.OSVersion.Version} ({(Environment.Is64BitProcess ? "64-bit" : "32-bit")} process)");
        sb.AppendLine($"Windows build  {Environment.OSVersion.VersionString}");
        sb.AppendLine($".NET           {Environment.Version}");
        sb.AppendLine($"CPU            {Environment.ProcessorCount} logical");
        try { sb.AppendLine($"Render tier    {System.Windows.Media.RenderCapability.Tier >> 16}"); } catch { }
        try { sb.AppendLine($"ADB            {ControlPhone.Adb.AdbClient.AdbPath}"); } catch (Exception e) { sb.AppendLine($"ADB            (not resolved: {e.Message})"); }
        try { sb.AppendLine($"FFmpeg         {ControlPhone.Video.Ffmpeg.VersionString(ControlPhone.Video.Ffmpeg.avcodec_version())}"); } catch (Exception e) { sb.AppendLine($"FFmpeg         (not loaded: {e.Message})"); }
        sb.AppendLine($"scrcpy-server  5.0");
        sb.AppendLine($"Data folder    {Store.DataDir}");
        sb.AppendLine($"Logs folder    {Path.GetDirectoryName(Crash.LogPath)}");
        sb.AppendLine($"Generated      {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        return sb.ToString();
    }

    static string Devices(IEnumerable<string>? lines)
    {
        var list = lines?.ToList() ?? [];
        if (list.Count == 0) return "(no phones connected)\n";
        var sb = new StringBuilder();
        sb.AppendLine($"# {list.Count} phone(s) — number, label, transport, state, Android");
        foreach (var l in list) sb.AppendLine(l);
        return sb.ToString();
    }

    static void Put(ZipArchive zip, string name, string text)
    {
        var e = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
        w.Write(Scrub(text));
    }

    static void Copy(ZipArchive zip, string name, string file)
    {
        try
        {
            if (!File.Exists(file)) return;
            var text = File.ReadAllText(file);
            if (text.Length > 2_000_000) text = text[..2_000_000] + "\n… (truncated)";
            Put(zip, name, text);
        }
        catch (Exception e) { Put(zip, name + ".error.txt", e.Message); }
    }
}
