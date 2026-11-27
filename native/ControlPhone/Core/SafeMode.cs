using System.IO;
using System.Text.Json;

namespace ControlPhone.Core;

/// <summary>Trạng thái giữa hai lần chạy: dùng để phát hiện khởi động hỏng liên tiếp.</summary>
public sealed class SessionState
{
    public string? LastStart { get; set; }
    public string? LastReady { get; set; }
    public int Fails { get; set; }
}

/// <summary>
/// Theo dõi một lần chạy: ghi «đang khởi động» lúc mở, «đã sẵn sàng» khi cửa sổ lên xong. Nếu lần trước có mở mà không
/// bao giờ sẵn sàng (tức là đã chết giữa lúc khởi động), bộ đếm tăng — hai lần liên tiếp thì bật chế độ an toàn, để một
/// sự cố đồ hoạ/cấu hình không khoá người dùng khỏi ứng dụng vĩnh viễn.
/// </summary>
public static class SessionGuard
{
    public static string DefaultPath => Path.Combine(Store.DataDir, "logs", "session.json");

    /// <summary>Hàm thuần: lần chạy trước mở mà không sẵn sàng ⇒ +1, ngược lại về 0.</summary>
    public static int FailsAfterStart(SessionState? prev) =>
        prev is { LastStart: not null, LastReady: null } ? Math.Max(0, prev.Fails) + 1 : 0;

    public static bool ShouldUseSafeMode(int fails) => fails >= 2;

    public static SessionState? Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<SessionState>(File.ReadAllText(path));
        }
        catch { return null; }
    }

    /// <summary>Ghi «đang khởi động» và trả về số lần hỏng liên tiếp (0 = lần trước đã sẵn sàng).</summary>
    public static int Begin(string? path = null)
    {
        path ??= DefaultPath;
        var fails = FailsAfterStart(Read(path));
        Write(path, new SessionState { LastStart = Now(), LastReady = null, Fails = fails });
        return fails;
    }

    public static void Ready(string? path = null)
    {
        path ??= DefaultPath;
        var st = Read(path) ?? new SessionState();
        st.LastReady = Now();
        st.Fails = 0;
        Write(path, st);
    }

    /// <summary>Thoát bình thường: không để lại dấu hiệu hỏng cho lần sau.</summary>
    public static void Clean(string? path = null)
    {
        path ??= DefaultPath;
        var st = Read(path) ?? new SessionState();
        st.LastReady ??= Now();
        st.Fails = 0;
        Write(path, st);
    }

    static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    static void Write(string path, SessionState st)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, JsonSerializer.Serialize(st));
        }
        catch { }
    }
}

/// <summary>
/// Chế độ an toàn: hồ sơ hình ảnh nhẹ nhất, bỏ qua thông số riêng của từng máy và không ưu tiên bộ mã hoá phần cứng —
/// chỉ áp dụng cho phiên hiện tại, không ghi vào config.json, nên mở lại bình thường là trở về như cũ.
/// </summary>
public static class SafeMode
{
    public const int LightMaxSize = 240;
    public const int LightFps = 5;
    public const int LightBitRate = 300_000;
    public const int LightHqMaxSize = 640;
    public const int LightHqFps = 10;
    public const int LightHqBitRate = 1_000_000;

    public static bool Active { get; private set; }
    public static int TriggerFails { get; private set; }

    public static void Activate(int fails)
    {
        Active = true;
        TriggerFails = fails;
        OpLog.Add($"Chế độ an toàn: {fails} lần khởi động trước không hoàn tất — dùng hồ sơ hình ảnh nhẹ cho phiên này", error: true);
    }

    public static void Reset() { Active = false; TriggerFails = 0; }

    /// <summary>Không bao giờ làm nặng thêm cài đặt của người dùng: chỉ lấy mức nhẹ hơn.</summary>
    public static int Light(int value, int cap) => value <= 0 ? cap : Math.Min(value, cap);
}
