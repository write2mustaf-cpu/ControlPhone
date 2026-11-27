using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace ControlPhone.Core;

/// <summary>
/// Bắt mọi lỗi không xử lý được (giao diện, luồng nền, Task) → ghi vào data\logs\crash.log (xoay vòng, đã che khoá)
/// và một dòng trong nhật ký hoạt động, để người dùng gửi kèm khi báo lỗi thay vì ứng dụng biến mất không dấu vết.
/// </summary>
public static class Crash
{
    public const long MaxBytes = 512 * 1024;
    const int MaxStack = 4000;

    public static string LogPath => Path.Combine(Store.DataDir, "logs", "crash.log");

    /// <summary>Ghi một lỗi; trả về phần chi tiết vừa ghi (đã che khoá) để hiện cho người dùng.</summary>
    public static string Report(Exception? e, string source)
    {
        var text = Format(e, source);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            Rotate();
            File.AppendAllText(LogPath, text + Environment.NewLine);
        }
        catch { }
        try { OpLog.Add($"Lỗi ({source}): {(e == null ? "unknown" : e.GetType().Name + ": " + OneLine(e.Message))}", error: true); } catch { }
        return text;
    }

    public static string Format(Exception? e, string source)
    {
        var sb = new StringBuilder();
        sb.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("] ").Append(source);
        try { sb.Append(" · ControlPhone ").Append(typeof(Crash).Assembly.GetName().Version?.ToString(3)); } catch { }
        sb.Append(" · Windows ").Append(Environment.OSVersion.Version).Append(" · .NET ").Append(Environment.Version);
        sb.AppendLine();
        for (var cur = e; cur != null; cur = cur.InnerException)
        {
            sb.AppendLine($"{cur.GetType().FullName}: {Ai.AiSecret.Redact(OneLine(cur.Message), null)}");
            var stack = cur.StackTrace ?? "";
            if (stack.Length > MaxStack) stack = stack[..MaxStack] + "…";
            if (stack.Length > 0) sb.AppendLine(stack);
        }
        return Ai.AiSecret.Redact(sb.ToString().TrimEnd(), null);
    }

    static string OneLine(string? s) => (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim();

    /// <summary>Giữ tối đa 2 tệp: crash.log và crash.log.1 (tệp cũ), mỗi tệp tối đa <see cref="MaxBytes"/>.</summary>
    public static void Rotate()
    {
        try
        {
            if (!File.Exists(LogPath)) return;
            if (new FileInfo(LogPath).Length < MaxBytes) return;
            var old = LogPath + ".1";
            if (File.Exists(old)) File.Delete(old);
            File.Move(LogPath, old);
        }
        catch { }
    }

    /// <summary>Gắn các bộ bắt lỗi toàn cục. Lỗi ở luồng giao diện được hiện hộp thoại và ứng dụng tiếp tục chạy.</summary>
    public static void Install(Application app)
    {
        app.DispatcherUnhandledException += (_, e) =>
        {
            var details = Report(e.Exception, "UI");
            e.Handled = true; // giữ ứng dụng sống: một lỗi trong một khung không được làm mất cả phiên điều khiển
            try { ShowDialog(details); } catch { }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Report(e.ExceptionObject as Exception, "background");
        TaskScheduler.UnobservedTaskException += (_, e) => { Report(e.Exception, "task"); e.SetObserved(); };
    }

    static void ShowDialog(string details)
    {
        var head = I18n.T("ControlPhone gặp lỗi không mong muốn") + "\n\n" +
                   I18n.T("Ứng dụng vẫn tiếp tục chạy. Chi tiết đã được ghi vào tệp nhật ký lỗi.") + "\n" + LogPath +
                   "\n\n" + I18n.T("Mở thư mục nhật ký?");
        var answer = MessageBox.Show(head, "ControlPhone", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer == MessageBoxResult.Yes) OpenLogFolder();
        _ = details;
    }

    public static void OpenLogFolder()
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath)!;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{LogPath}\"") { UseShellExecute = true });
        }
        catch
        {
            try { Process.Start(new ProcessStartInfo(Path.GetDirectoryName(LogPath)!) { UseShellExecute = true }); } catch { }
        }
    }
}
