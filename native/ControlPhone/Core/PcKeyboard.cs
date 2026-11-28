using System.IO;
using ControlPhone.Adb;

namespace ControlPhone.Core;

/// <summary>
/// Chế độ "chỉ nhập từ PC": cài + chọn bàn phím ControlPhone (native/keyboard) — một bàn phím không bao giờ hiện
/// trên màn hình điện thoại; phím, chữ và lệnh dán từ PC vẫn đi thẳng vào ô nhập. Tắt chế độ = trả lại bàn phím cũ.
/// </summary>
public static class PcKeyboard
{
    public const string Package = "com.controlphone.keyboard";
    public const string Ime = Package + "/.PcKeyboard";
    static readonly string Apk = Path.Combine(AppContext.BaseDirectory, "ControlPhoneKeyboard.apk");

    static async Task<string> Sh(string addr, string cmd, int ms = 15000) => (await AdbClient.ShellAsync(addr, cmd, ms)).Trim();

    public static async Task<string> CurrentIme(string addr) => await Sh(addr, "settings get secure default_input_method");

    /// <summary>Bật chế độ. Trả về (ok, thông báo, bàn phím trước đó).</summary>
    public static async Task<(bool Ok, string Msg, string? Prev)> EnableAsync(string addr)
    {
        var prev = await CurrentIme(addr);
        if ((await Sh(addr, $"pm path {Package}")) == "")
        {
            if (!File.Exists(Apk)) return (false, "Không thấy ControlPhoneKeyboard.apk", null);
            var r = await AdbClient.RunAsync([.. AdbClient.Sel(addr), "install", "-r", Apk], 120000);
            if (!r.Out.Contains("Success")) return (false, "Không cài được bàn phím: " + r.Out, null);
        }
        // Android đăng ký bàn phím mới sau khi cài vài giây → thử lại
        string o = "";
        for (int i = 0; i < 15; i++)
        {
            o = await Sh(addr, $"ime enable {Ime} 2>&1");
            if (!o.Contains("Unknown input method")) break;
            await Task.Delay(1000);
        }
        if (o.Contains("Unknown input method")) return (false, o, null);
        await Sh(addr, $"ime set {Ime} 2>&1");
        var now = await CurrentIme(addr);
        if (now != Ime) return (false, "Không chọn được bàn phím: " + now, null);
        return (true, "Đã tắt bàn phím điện thoại — chỉ nhập từ PC", prev == Ime ? null : prev);
    }

    /// <summary>
    /// Thứ tự bàn phím sẽ thử khôi phục khi tắt chế độ. Bàn phím đã lưu được ưu tiên, nhưng CHỈ khi nó còn tồn tại:
    /// người dùng có thể đã gỡ nó (hoặc bản ROM đổi tên), và khi đó "ime set" thất bại trong im lặng — điện thoại ở lại
    /// với bàn phím ControlPhone vốn không hiện trên màn hình, tức là không gõ được ở đâu cả. Danh sách rỗng = không có
    /// bàn phím nào khác để quay về.
    /// </summary>
    public static List<string> RestoreOrder(string? prev, IEnumerable<string>? available)
    {
        var all = (available ?? [])
            .Select(x => (x ?? "").Trim())
            .Where(x => x != "" && x != "null" && !x.Equals(Ime, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var saved = (prev ?? "").Trim();
        var order = new List<string>();
        if (saved != "" && saved != "null" && all.Contains(saved, StringComparer.OrdinalIgnoreCase))
            order.Add(all.First(x => x.Equals(saved, StringComparison.OrdinalIgnoreCase)));
        order.AddRange(all.Where(x => !x.Contains("voice", StringComparison.OrdinalIgnoreCase)));
        order.AddRange(all.Where(x => x.Contains("voice", StringComparison.OrdinalIgnoreCase)));
        return order.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Tắt chế độ: trả lại bàn phím cũ, hoặc bàn phím khác bất kỳ còn cài trên máy.</summary>
    public static async Task<(bool Ok, string Msg)> DisableAsync(string addr, string? prev)
    {
        var enabled = Lines(await Sh(addr, "ime list -s"));
        // "ime list -a" liệt kê cả bàn phím đã cài nhưng đang tắt — chúng vẫn dùng được sau khi bật lại.
        var installed = Lines(await Sh(addr, "ime list -a -s"));
        var order = RestoreOrder(prev, [.. enabled, .. installed]);
        if (order.Count == 0)
            return (false, "Máy không có bàn phím nào khác — cài bàn phím trước rồi mới tắt chế độ này (bàn phím ControlPhone không hiện trên màn hình)");
        foreach (var target in order)
        {
            if (!enabled.Contains(target, StringComparer.OrdinalIgnoreCase)) await Sh(addr, $"ime enable {target} 2>&1");
            await Sh(addr, $"ime set {target} 2>&1");
            if (await CurrentIme(addr) != target) continue;
            await Sh(addr, $"ime disable {Ime} 2>&1");
            return (true, "Đã bật lại bàn phím điện thoại: " + target);
        }
        // Không bàn phím nào nhận được: giữ nguyên bàn phím ControlPhone (tắt nó đi thì máy không còn bàn phím).
        return (false, "Không chọn lại được bàn phím nào (" + string.Join(", ", order) + ") — vẫn giữ bàn phím ControlPhone");
    }

    static List<string> Lines(string text) =>
        text.Split('\n').Select(x => x.Trim()).Where(x => x != "").ToList();

    /// <summary>Khi máy kết nối lại: nếu đang bật chế độ mà bàn phím bị đổi → chọn lại bàn phím ControlPhone.</summary>
    public static async Task EnsureAsync(string addr)
    {
        if (await CurrentIme(addr) == Ime) return;
        await EnableAsync(addr);
    }
}
