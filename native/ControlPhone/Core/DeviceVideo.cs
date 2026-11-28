namespace ControlPhone.Core;

/// <summary>Thông số hình ảnh thực tế dùng để mở một luồng scrcpy (đã áp dụng ghi đè riêng của từng máy).</summary>
public sealed record VideoPlan(int MaxSize, int BitRate, int MaxFps);

/// <summary>
/// Cài đặt hình ảnh riêng cho từng điện thoại: một máy yếu (480p) đứng cạnh một máy mạnh (1080p) trong cùng một lưới.
/// Giá trị để trống = dùng cài đặt chung. Hàm thuần để bản tự kiểm tra gọi trực tiếp.
/// </summary>
public static class DeviceVideo
{
    /// <summary>Mức người dùng đặt (ghi đè riêng của máy, không có thì cài đặt chung) — bỏ qua mức đã học.</summary>
    public static VideoPlan ThumbBase(DeviceMeta? m, Settings s)
    {
        var plan = new VideoPlan(
            Pick(m?.ThumbMaxSize, null, s.ThumbMaxSize, 160, 1920),
            Pick(m?.ThumbBitRate, null, s.ThumbBitRate, 30_000, 20_000_000),
            Pick(m?.ThumbFps, null, s.ThumbFps, 1, 60));
        return SafeMode.Active
            ? plan with
            {
                MaxSize = SafeMode.Light(plan.MaxSize, SafeMode.LightMaxSize),
                BitRate = SafeMode.Light(plan.BitRate, SafeMode.LightBitRate),
                MaxFps = SafeMode.Light(plan.MaxFps, SafeMode.LightFps),
            }
            : plan;
    }

    /// <summary>Mức dùng để mở luồng lưới: mức gốc, nhưng không cao hơn mức đã học được cho máy này.</summary>
    public static VideoPlan Thumb(DeviceMeta? m, Settings s)
    {
        var plan = ThumbBase(m, s);
        if (m == null) return plan;
        var tuned = plan with
        {
            MaxSize = Tune(m.ThumbMaxSize, m.AutoMaxSize, plan.MaxSize, 160, 1920),
            BitRate = Tune(m.ThumbBitRate, m.AutoBitRate, plan.BitRate, 30_000, 20_000_000),
            MaxFps = Tune(m.ThumbFps, m.AutoFps, plan.MaxFps, 1, 60),
        };
        return LoadBudget.Apply(tuned);
    }

    /// <summary>Máy đã được đặt tay thì giữ nguyên; chưa đặt thì dùng mức đã học (không cao hơn mức gốc).</summary>
    static int Tune(int? manual, int? learned, int current, int min, int max) =>
        manual is > 0 ? current
        : learned is int l && l > 0 ? Math.Min(current, Math.Clamp(l, min, max))
        : current;

    public static VideoPlan Hq(DeviceMeta? m, Settings s)
    {
        var plan = new VideoPlan(
            Pick(m?.HqMaxSize, null, s.HqMaxSize, 320, 2560),
            Pick(m?.HqBitRate, null, s.HqBitRate, 500_000, 40_000_000),
            Pick(m?.HqFps, null, s.HqFps, 5, 60));
        return SafeMode.Active
            ? plan with
            {
                MaxSize = SafeMode.Light(plan.MaxSize, SafeMode.LightHqMaxSize),
                BitRate = SafeMode.Light(plan.BitRate, SafeMode.LightHqBitRate),
                MaxFps = SafeMode.Light(plan.MaxFps, SafeMode.LightHqFps),
            }
            : plan;
    }

    /// <summary>Ghi đè hợp lệ (&gt; 0 và trong khoảng) thì dùng, rồi tới mức đã học, cuối cùng là giá trị chung.</summary>
    static int Pick(int? over, int? learned, int global, int min, int max)
    {
        var v = over is int o && o > 0 ? o : learned is int l && l > 0 ? l : global;
        return Math.Clamp(v, min, max);
    }

    public static bool HasOverrides(DeviceMeta? m) =>
        m != null && (m.ThumbFps != null || m.ThumbMaxSize != null || m.ThumbBitRate != null || m.HqFps != null || m.HqMaxSize != null || m.HqBitRate != null);

    public static bool HasLearned(DeviceMeta? m) =>
        m != null && (m.AutoFps != null || m.AutoMaxSize != null || m.AutoBitRate != null);
}
