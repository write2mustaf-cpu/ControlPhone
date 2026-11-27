namespace ControlPhone.Core;

/// <summary>
/// Nhật ký hoạt động ngắn trong bộ nhớ (kết nối / mất kết nối, thao tác hàng loạt, lỗi) để trợ lý AI và hộp thoại
/// chẩn đoán đọc được tình trạng thật gần đây mà không phải mở tệp log. Giữ 600 dòng gần nhất.
/// </summary>
public static class OpLog
{
    public sealed record Entry(DateTime At, string Text, bool Error);

    const int Cap = 600;
    static readonly LinkedList<Entry> items = new();
    static readonly object gate = new();

    public static void Add(string text, bool error = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        lock (gate)
        {
            items.AddLast(new Entry(DateTime.Now, text.Trim(), error));
            while (items.Count > Cap) items.RemoveFirst();
        }
    }

    public static List<Entry> Tail(int count)
    {
        lock (gate) return items.Reverse().Take(Math.Clamp(count, 1, Cap)).Reverse().ToList();
    }

    public static string Format(int count) =>
        string.Join("\n", Tail(count).Select(e => $"{e.At:HH:mm:ss} {(e.Error ? "ERR" : "   ")} {e.Text}"));
}
