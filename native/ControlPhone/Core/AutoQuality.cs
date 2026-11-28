using ControlPhone.Video;

namespace ControlPhone.Core;

/// <summary>
/// Vòng điều chỉnh chất lượng thích ứng: mỗi 2 giây đọc số đo thật của từng luồng (khung/giây, thời gian giải mã) và mức tải
/// CPU của máy tính, rồi hạ hoặc nâng mức của riêng máy đó. Mức đã học được lưu vào hồ sơ máy (AutoMaxSize/AutoFps/AutoBitRate)
/// nên lần mở luồng sau dùng lại ngay — không bao giờ ghi đè mức người dùng đặt tay.
/// </summary>
public static class AutoQuality
{
    // mức gốc đi kèm: người dùng đổi cài đặt chung thì bộ điều chỉnh được dựng lại theo mức mới
    static readonly Dictionary<string, (QualityGovernor Gov, VideoPlan Base)> Governors = new(StringComparer.Ordinal);
    static readonly object Gate = new();
    static TimeSpan lastCpu;
    static long lastCpuAt;
    static double cpuLoad;

    /// <summary>Tải CPU của tiến trình này (%, 0–100) tính từ lần lấy mẫu trước.</summary>
    public static double CpuLoad()
    {
        try
        {
            using var p = System.Diagnostics.Process.GetCurrentProcess();
            var now = Environment.TickCount64;
            var cpu = p.TotalProcessorTime;
            var at = Interlocked.Read(ref lastCpuAt);
            if (at != 0)
            {
                var ms = Math.Max(1, now - at);
                var used = (cpu - lastCpu).TotalMilliseconds;
                cpuLoad = Math.Clamp(used / (ms * Environment.ProcessorCount) * 100, 0, 100);
            }
            Interlocked.Exchange(ref lastCpu, cpu);
            Interlocked.Exchange(ref lastCpuAt, now);
        }
        catch { }
        return cpuLoad;
    }

    public static QualityGovernor? Governor(PhoneDevice d, int streams)
    {
        if (d.ThumbDecoder == null) return null;
        var meta = Store.MetaOrNull(d.Id);
        // trần = mức người dùng đặt, nhưng không vượt phần ngân sách giải mã mà máy tính chia cho từng luồng
        var basePlan = LoadBudget.Apply(DeviceVideo.ThumbBase(meta, Store.Settings));
        lock (Gate)
        {
            if (Governors.TryGetValue(d.Id, out var entry) && entry.Base == basePlan) return entry.Gov;
            var start = new QualityTarget(
                meta?.AutoMaxSize ?? basePlan.MaxSize,
                meta?.AutoBitRate ?? basePlan.BitRate,
                meta?.AutoFps ?? basePlan.MaxFps);
            var gov = new QualityGovernor(start, FloorOf(basePlan), new QualityTarget(basePlan.MaxSize, basePlan.BitRate, basePlan.MaxFps));
            Governors[d.Id] = (gov, basePlan);
            return gov;
        }
    }

    /// <summary>Sàn: vẫn nhìn được nhưng rẻ — một nửa độ phân giải, một phần tư bitrate, một phần ba fps của mức gốc.</summary>
    public static QualityTarget FloorOf(VideoPlan p) => new(
        Math.Max(240, p.MaxSize / 2),
        Math.Max(120_000, p.BitRate / 4),
        Math.Max(5, p.MaxFps / 3));

    /// <summary>Một bước cho một máy. Trả về null khi tắt tính năng, chưa đủ mẫu, hoặc không có gì đổi.</summary>
    public static QualityDecision? Observe(PhoneDevice d, long nowMs) => Observe(d, nowMs, CpuLoad(), 1);

    /// <summary>Như trên nhưng dùng mức tải CPU đã lấy sẵn cho cả nhịp (300 máy = 1 lần đọc, không phải 300).</summary>
    public static QualityDecision? Observe(PhoneDevice d, long nowMs, double cpu, int streams)
    {
        if (!Store.Settings.AutoQuality) return null;
        var stats = d.ThumbDecoder?.Stats;
        var meta = Store.MetaOrNull(d.Id);
        var gov = Governor(d, streams);
        if (stats == null || meta == null || gov == null) return null;
        var snap = stats.Snapshot(nowMs);
        if (snap.Frames < 30) return null;                     // chưa đủ mẫu để kết luận
        var signal = new QualitySignal(snap.Fps, snap.DecodeMs, cpu, d.Transport != "wifi", 1, snap.AgeMs);
        var decision = gov.Step(signal, nowMs);
        if (!decision.Changed) return decision;
        var basePlan = DeviceVideo.ThumbBase(meta, Store.Settings);
        var learned = decision.Target;
        bool atBase = learned.MaxSize >= basePlan.MaxSize && learned.MaxFps >= basePlan.MaxFps && learned.BitRate >= basePlan.BitRate;
        meta.AutoMaxSize = atBase ? null : learned.MaxSize;
        meta.AutoFps = atBase ? null : learned.MaxFps;
        meta.AutoBitRate = atBase ? null : learned.BitRate;
        Store.Save();
        DeviceLog.Add(d.Id, $"quality {learned.MaxSize}p/{learned.MaxFps}fps/{learned.BitRate / 1000}kbps — {decision.Reason}");
        return decision;
    }

    /// <summary>Xoá mức đã học của một máy (dùng khi người dùng muốn quay lại mức gốc).</summary>
    public static void Forget(PhoneDevice d)
    {
        var meta = Store.MetaOrNull(d.Id);
        if (meta != null) { meta.AutoMaxSize = meta.AutoFps = meta.AutoBitRate = null; Store.Save(); }
        lock (Gate) Governors.Remove(d.Id);
    }

    public static void ForgetAll()
    {
        foreach (var m in Store.State.Devices.Values) { m.AutoMaxSize = m.AutoFps = m.AutoBitRate = null; }
        lock (Gate) Governors.Clear();
        Store.Save();
    }

    public static string Describe(PhoneDevice d)
    {
        var gov = Governor(d, 1);
        if (gov == null) return "";
        var c = gov.Current;
        return $"{c.MaxSize}p · {c.MaxFps} fps · {c.BitRate / 1000} kbps — {gov.LastReason}";
    }
}
