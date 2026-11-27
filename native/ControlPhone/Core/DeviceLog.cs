using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ControlPhone.Core;

/// <summary>
/// Nhật ký riêng của từng điện thoại: trạng thái kết nối, lỗi luồng hình và kết quả thao tác trên máy đó. Giữ 300 dòng
/// gần nhất trong bộ nhớ để hộp thoại và trợ lý AI đọc ngay, đồng thời ghi nối vào
/// data\logs\devices\&lt;id&gt;.log (xoay vòng 256 KB) để còn xem lại sau khi đóng ứng dụng.
/// </summary>
public static class DeviceLog
{
    public sealed record Entry(DateTime At, string Text, bool Error);

    const int Cap = 300;
    public const long MaxFileBytes = 256 * 1024;

    static readonly Dictionary<string, LinkedList<Entry>> mem = new(StringComparer.Ordinal);
    static readonly object gate = new();

    public static string Dir => Path.Combine(Store.DataDir, "logs", "devices");

    /// <summary>Tên tệp an toàn: số sê-ri có thể chứa ':' (Wi-Fi), '/' hoặc ký tự cấm của Windows.</summary>
    public static string PathFor(string id) => Path.Combine(Dir, SafeName(id) + ".log");

    public static string SafeName(string id)
    {
        var s = Regex.Replace(id ?? "", @"[^\w.\-]", "_");
        s = s.Trim('.', '_');
        if (s.Length == 0) s = "device";
        return s.Length > 60 ? s[..60] : s;
    }

    public static void Add(string id, string text, bool error = false)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(text)) return;
        var e = new Entry(DateTime.Now, text.Trim(), error);
        lock (gate)
        {
            if (!mem.TryGetValue(id, out var list)) mem[id] = list = new LinkedList<Entry>();
            list.AddLast(e);
            while (list.Count > Cap) list.RemoveFirst();
        }
        try
        {
            var path = PathFor(id);
            Directory.CreateDirectory(Dir);
            Rotate(path);
            File.AppendAllText(path, $"{e.At:yyyy-MM-dd HH:mm:ss} {(error ? "ERR" : "   ")} {e.Text}{Environment.NewLine}", Encoding.UTF8);
        }
        catch { }
    }

    public static List<Entry> Tail(string id, int count)
    {
        lock (gate)
            return mem.TryGetValue(id, out var list)
                ? list.Reverse().Take(Math.Clamp(count, 1, Cap)).Reverse().ToList()
                : [];
    }

    public static string Format(string id, int count) =>
        string.Join("\n", Tail(id, count).Select(e => $"{e.At:HH:mm:ss} {(e.Error ? "ERR" : "   ")} {e.Text}"));

    /// <summary>Giữ tối đa 2 tệp mỗi máy: &lt;id&gt;.log và &lt;id&gt;.log.1.</summary>
    public static void Rotate(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length < MaxFileBytes) return;
            var old = path + ".1";
            if (File.Exists(old)) File.Delete(old);
            File.Move(path, old);
        }
        catch { }
    }

    /// <summary>Xoá nhật ký của một máy (bộ nhớ và tệp).</summary>
    public static void Clear(string id)
    {
        lock (gate) mem.Remove(id);
        try
        {
            var path = PathFor(id);
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".1")) File.Delete(path + ".1");
        }
        catch { }
    }
}
