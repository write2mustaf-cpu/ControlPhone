using System.Text;
using ControlPhone.Core;
using ControlPhone.Scrcpy;
using ControlPhone.Adb;

namespace ControlPhone;

/// <summary>
/// Gõ chữ vào điện thoại. Có phiên xem (scrcpy) thì gửi qua phiên — phiên chuyển chữ Unicode thành clipboard rồi dán,
/// nên tiếng Việt/Ả Rập/Trung đều vào được. Không có phiên thì chỉ gõ được ASCII bằng `adb shell input text`.
/// </summary>
public static class Typing
{
    /// <summary>Độ dài tối đa gửi thẳng bằng thông điệp INJECT_TEXT của scrcpy.</summary>
    public const int MaxDirect = 300;

    /// <summary>Chữ ASCII in được, đủ ngắn: gửi thẳng, không cần clipboard.</summary>
    public static bool IsPlainAscii(string s) =>
        s.Length is > 0 and <= MaxDirect && s.All(c => c >= 0x20 && c <= 0x7e);

    /// <summary>Chữ cần phiên xem vì adb không gõ được (Unicode, nhiều dòng, hoặc quá dài).</summary>
    public static bool NeedsSession(string s) => !IsPlainAscii(s);

    /// <summary>Chữ đưa cho `adb shell input text`: dấu cách là %s, ký tự có nghĩa với shell được thoát.</summary>
    public static string AdbArg(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (var c in s)
        {
            if (c is '\r' or '\n' or '\t') { sb.Append("%s"); continue; }
            if (c < 0x20) continue;
            if (c == ' ') { sb.Append("%s"); continue; }
            if ("\"'`$&|;<>()*?[]{}~#!\\".Contains(c)) sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>adb chỉ gõ được khi chữ là ASCII một dòng (bỏ qua ký tự điều khiển).</summary>
    public static bool CanUseAdb(string s) =>
        s.Length > 0 && s.All(c => (c >= 0x20 && c <= 0x7e) || c is '\r' or '\n' or '\t');

    public static string Preview(string s, int max = 40)
    {
        var one = s.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return one.Length > max ? one[..max] + "…" : one;
    }

    /// <summary>
    /// Tóm tắt kết quả gõ chữ: OK khi mọi máy nhận, FAIL khi không máy nào, PARTIAL khi chỉ một phần — kèm một dòng mỗi máy
    /// để trợ lý không thể nói "đã gõ xong" khi thực tế có máy không nhận.
    /// </summary>
    public static string Summary(string text, IReadOnlyCollection<(string Label, bool Ok, string Msg)> results)
    {
        int ok = results.Count(r => r.Ok);
        var head = results.Count == 0 ? "FAIL" : ok == results.Count ? "OK" : ok == 0 ? "FAIL" : "PARTIAL";
        var sb = new StringBuilder();
        sb.Append($"{head}: typed \"{Preview(text)}\" on {ok}/{results.Count} phone(s)");
        foreach (var r in results) sb.Append("\n  ").Append(r.Ok ? "OK" : "FAIL").Append(' ').Append(r.Label).Append(" — ").Append(r.Msg);
        return sb.ToString();
    }

    /// <summary>Gửi chữ tới một máy. Trả về (ok, mô tả đường đi hoặc lý do lỗi).</summary>
    public static async Task<(bool Ok, string Msg)> SendAsync(PhoneDevice d, string text, bool enter = false)
    {
        if (string.IsNullOrEmpty(text)) return (false, I18n.T("Không có chữ để gõ"));
        if (!d.IsOnline) return (false, I18n.T("Máy đang không kết nối"));
        if (d.ControlSession is { } s)
        {
            s.TypeText(text, paste: NeedsSession(text));
            if (enter) _ = Task.Delay(150).ContinueWith(_ => s.KeyPress(AndroidKey.Enter));
            return (true, I18n.T("qua phiên xem"));
        }
        if (!CanUseAdb(text))
            return (false, I18n.T("Chữ này cần phiên xem của máy (mở xem hoặc màn hình lớn): adb chỉ gõ được ASCII"));
        if (d.ActiveAddr is not { } addr) return (false, I18n.T("Máy chưa sẵn sàng"));
        await AdbClient.ShellAsync(addr, "input text " + AdbArg(text), 15000);
        if (enter) await AdbClient.ShellAsync(addr, "input keyevent 66", 10000);
        return (true, I18n.T("qua adb"));
    }
}
