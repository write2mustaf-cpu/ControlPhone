using System.IO;

namespace ControlPhone.Core;

/// <summary>
/// Nhật ký khởi động: mỗi mốc một dòng (bắt đầu, đọc dữ liệu, tạo cửa sổ, hiện cửa sổ, sẵn sàng, thoát) kèm thời gian từ
/// lúc tiến trình bắt đầu. Ứng dụng "tự tắt" mà không có thông báo thường là chết ở một mốc nào đó — tệp này chỉ ra mốc ấy,
/// và <see cref="Crash"/> chép 8 mốc cuối vào mọi báo lỗi.
/// </summary>
public static class StartupTrace
{
    public const long MaxBytes = 256 * 1024;
    static readonly DateTime Origin = DateTime.Now;
    static readonly object Gate = new();

    public static string Path => System.IO.Path.Combine(Store.DataDir, "logs", "startup.log");

    /// <summary>Hàm thuần: một dòng nhật ký.</summary>
    public static string Format(DateTime at, double elapsedMs, string stage, string? extra = null) =>
        $"{at:yyyy-MM-dd HH:mm:ss.fff} +{elapsedMs:0} ms · {stage}{(string.IsNullOrWhiteSpace(extra) ? "" : " · " + extra)}";

    public static void Mark(string stage, string? extra = null)
    {
        var line = Format(DateTime.Now, (DateTime.Now - Origin).TotalMilliseconds, stage, extra);
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                Rotate();
                File.AppendAllText(Path, line + Environment.NewLine);
            }
        }
        catch { }
    }

    static void Rotate()
    {
        try
        {
            if (!File.Exists(Path) || new FileInfo(Path).Length < MaxBytes) return;
            var old = Path + ".1";
            if (File.Exists(old)) File.Delete(old);
            File.Move(Path, old);
        }
        catch { }
    }

    /// <summary>Các mốc gần nhất (dùng cho báo lỗi và gói chẩn đoán).</summary>
    public static string[] Tail(int lines = 8)
    {
        try
        {
            if (!File.Exists(Path)) return [];
            var all = File.ReadAllLines(Path);
            return all.Length <= lines ? all : all[^lines..];
        }
        catch { return []; }
    }
}
