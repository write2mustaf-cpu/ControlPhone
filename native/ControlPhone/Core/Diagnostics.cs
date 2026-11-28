namespace ControlPhone.Core;

/// <summary>Kết luận chẩn đoán: mã lỗi, mức độ, nguyên nhân, cách sửa, và có sửa được tự động hay không.</summary>
public sealed record Diagnosis(string Code, string Severity, string Cause, string Fix, bool AutoFixable, int Hits);

/// <summary>
/// Đọc nhật ký của một máy rồi chỉ ra nguyên nhân gốc. Hàm thuần trên danh sách dòng nên kiểm tra được, và dùng chung cho
/// hộp thoại chẩn đoán lẫn công cụ "diagnose_device" của trợ lý AI — nhờ vậy AI không phải đoán từ log thô.
/// </summary>
public static class Diagnostics
{
    sealed record Rule(string Code, string Severity, string[] Needles, string Cause, string Fix, bool AutoFixable);

    static readonly Rule[] Rules =
    [
        new("unauthorized", "error", ["unauthorized", "authorizing"],
            "The phone has not accepted the ADB key of this PC (the RSA dialog was dismissed or never shown).",
            "Unlock the phone, plug the cable, accept 'Allow USB debugging', and enable 'Always allow from this computer'.", false),
        new("cmdline-stack", "error", ["stack corruption", "ACodec"],
            "The scrcpy-server command line is longer than the buffer this phone's encoder can hold (seen on Samsung/Exynos).",
            "ControlPhone already shortens the command line automatically; if it repeats, lower the resolution of this phone.", true),
        new("adb-port", "error", ["address already in use", "could not install *smartsocket"],
            "Another adb server (Android Studio, a vendor tool) owns port 5037.",
            "Close the other tool, or set a different ANDROID_ADB_SERVER_PORT for it.", false),
        new("offline", "error", ["device offline", "error: closed", "connection reset"],
            "The device dropped off the bus: bad cable/port, USB hub power, or the phone rebooted.",
            "Re-plug into another port (avoid unpowered hubs), then reconnect; Wi-Fi devices may need 'adb reconnect'.", true),
        new("no-device", "error", ["no devices/emulators found", "cannot find any usb device", "device not found"],
            "ADB cannot see the phone at all.",
            "Check the cable, enable USB debugging, and confirm the phone appears in 'adb devices'.", false),
        new("server-died", "error", ["fatal signal", "sigsegv", "server died", "scrcpy server exited"],
            "The scrcpy-server process on the phone crashed.",
            "Reconnect the phone; if it repeats on one model, report the log — the command line is shortened automatically.", true),
        new("encoder", "warn", ["c2.android.", "omx.google.", "no output buffer", "encoder failed"],
            "Only the phone's software H.264 encoder is available, so decoding on this PC gets expensive.",
            "Lower this phone's grid resolution/fps, or switch it to hardware encoding in the device video settings.", true),
        new("keyframe", "warn", ["no key frame", "waiting for key frame", "send_packet -"],
            "The stream lost its reference frame (packet loss or a decoder flush).",
            "ControlPhone requests a fresh key frame automatically; if the picture stays frozen, restart this phone's stream.", true),
        new("permission", "error", ["insufficient permissions", "permission denial"],
            "The phone refuses the operation (SELinux or a vendor restriction on this action).",
            "Root the device or choose an operation that does not need it.", false),
        new("wifi", "warn", ["failed to connect", "connection refused", "timed out", "unreachable"],
            "The Wi-Fi path to the phone is gone (sleep, IP change, or the phone left the network).",
            "Keep the phone awake and on the same network; ControlPhone retries and falls back to USB automatically.", true),
    ];

    /// <summary>Quy tắc khớp với dòng cuối cùng trong nhật ký (lỗi mới nhất), kèm số lần xuất hiện.</summary>
    public static Diagnosis? Analyse(IEnumerable<string> lines)
    {
        var text = lines.ToList();
        if (text.Count == 0) return null;
        Diagnosis? best = null;
        var bestAt = -1;
        foreach (var rule in Rules)
        {
            var hits = 0;
            var last = -1;
            for (int i = 0; i < text.Count; i++)
            {
                var line = text[i];
                if (line.Length == 0) continue;
                if (!rule.Needles.Any(n => line.Contains(n, StringComparison.OrdinalIgnoreCase))) continue;
                hits++;
                last = i;
            }
            if (hits == 0 || last <= bestAt) continue;
            bestAt = last;
            best = new Diagnosis(rule.Code, rule.Severity, rule.Cause, rule.Fix, rule.AutoFixable, hits);
        }
        return best;
    }

    /// <summary>Chẩn đoán từ nhật ký còn trong bộ nhớ của một máy (300 dòng gần nhất).</summary>
    public static Diagnosis? ForDevice(string id) =>
        Analyse(DeviceLog.Tail(id, 300).Select(e => e.Text));
}
