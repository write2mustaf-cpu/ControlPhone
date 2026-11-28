namespace ControlPhone.Core;

/// <summary>Một mức chất lượng hình: độ phân giải tối đa (px), bitrate (bit/s), số khung/giây.</summary>
public sealed record QualityTarget(int MaxSize, int BitRate, int MaxFps);

/// <summary>Số đo dùng để quyết định: luồng này đang chạy thế nào và máy tính đang chịu tải ra sao.</summary>
public sealed record QualitySignal(double Fps, double DecodeMs, double CpuLoad, bool Usb, int Devices, double AgeMs = 0);

/// <summary>Kết quả một bước: mức mới, lý do (để ghi nhật ký và cho trợ lý AI đọc), có đổi hay không.</summary>
public sealed record QualityDecision(QualityTarget Target, string Reason, bool Changed);

/// <summary>
/// Bộ điều chỉnh chất lượng thích ứng cho MỘT máy. Hàm thuần theo thời gian bơm vào nên kiểm tra được: hai lần liên tiếp
/// quá tải mới hạ (tránh nhảy vì một khung chậm), có thời gian chờ giữa hai lần đổi, hạ theo thứ tự bitrate → fps → độ phân
/// giải và nâng ngược lại khi đã khoẻ, không bao giờ ra ngoài khoảng [floor, ceiling].
/// </summary>
public sealed class QualityGovernor
{
    readonly QualityTarget floor, ceiling;
    int stress, healthy;
    long cooldownUntil;
    public QualityTarget Current { get; private set; }
    public string LastReason { get; private set; } = "start";

    public QualityGovernor(QualityTarget start, QualityTarget floor, QualityTarget ceiling)
    {
        this.floor = Normalize(floor);
        this.ceiling = Normalize(ceiling);
        Current = Normalize(start);
    }

    public QualityDecision Step(QualitySignal s, long nowMs)
    {
        // luồng đứng (máy ngủ, cáp lỏng) không phải "quá tải": hạ chất lượng lúc này chỉ làm hình mờ khi máy tỉnh lại
        if (s.AgeMs > 3000) return new QualityDecision(Current, "stream is stalled — quality left alone", false);
        var frameMs = 1000.0 / Math.Max(1, Current.MaxFps);
        var fpsLow = s.Fps > 0 && s.Fps < Current.MaxFps * 0.7;
        var slowDecode = s.DecodeMs > frameMs * 0.6;
        var hot = s.CpuLoad > 85 || (!s.Usb && s.Fps > 0 && s.Fps < Current.MaxFps * 0.5);
        var stressed = slowDecode || fpsLow || hot;
        var fine = s.DecodeMs > 0 && s.DecodeMs < frameMs * 0.35 && s.Fps > Current.MaxFps * 0.9 && s.CpuLoad < 70;

        stress = stressed ? stress + 1 : 0;
        healthy = fine ? healthy + 1 : 0;
        if (nowMs < cooldownUntil) return new QualityDecision(Current, LastReason, false);

        if (stress >= 2)
        {
            stress = 0; healthy = 0;
            var why = slowDecode ? "decode too slow" : fpsLow ? "frames arriving too slowly" : "machine under load";
            var next = Current;
            if (Current.BitRate > floor.BitRate) next = Current with { BitRate = Step(Current.BitRate, floor.BitRate, 0.70) };
            else if (Current.MaxFps > floor.MaxFps) next = Current with { MaxFps = Math.Max(floor.MaxFps, Current.MaxFps - Math.Max(1, Current.MaxFps / 4)) };
            else if (Current.MaxSize > floor.MaxSize) next = Current with { MaxSize = Step(Current.MaxSize, floor.MaxSize, 0.80) };
            if (next == Current) return new QualityDecision(Current, "at the floor: " + why, false);
            Current = next;
            cooldownUntil = nowMs + 4000;
            LastReason = $"lowered ({why})";
            return new QualityDecision(Current, LastReason, true);
        }

        if (healthy >= 4)
        {
            stress = 0; healthy = 0;
            var next = Current;
            if (Current.BitRate < ceiling.BitRate) next = Current with { BitRate = Step(Current.BitRate, ceiling.BitRate, 1.20) };
            else if (Current.MaxFps < ceiling.MaxFps) next = Current with { MaxFps = Math.Min(ceiling.MaxFps, Current.MaxFps + Math.Max(1, Current.MaxFps / 6)) };
            else if (Current.MaxSize < ceiling.MaxSize) next = Current with { MaxSize = Step(Current.MaxSize, ceiling.MaxSize, 1.20) };
            if (next == Current) return new QualityDecision(Current, "at the ceiling", false);
            Current = next;
            cooldownUntil = nowMs + 6000;
            LastReason = "raised (stream is healthy)";
            return new QualityDecision(Current, LastReason, true);
        }

        return new QualityDecision(Current, LastReason, false);
    }

    /// <summary>Nhân/chia nhưng luôn tiến ít nhất một bậc và không vượt mốc đích; độ phân giải giữ số chẵn.</summary>
    static int Step(int value, int bound, double factor)
    {
        var target = (int)Math.Round(value * factor);
        if (factor < 1 && target >= value) target = value - 1;
        if (factor > 1 && target <= value) target = value + 1;
        target = factor < 1 ? Math.Max(bound, target) : Math.Min(bound, target);
        if (value > 400 && target % 2 != 0 && bound % 2 == 0) target--;
        return target;
    }

    static QualityTarget Normalize(QualityTarget t) => new(
        Math.Clamp(t.MaxSize, 160, 2560),
        Math.Clamp(t.BitRate, 30_000, 40_000_000),
        Math.Clamp(t.MaxFps, 1, 60));
}
