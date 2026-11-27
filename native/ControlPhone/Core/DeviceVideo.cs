namespace ControlPhone.Core;

/// <summary>Thông số hình ảnh thực tế dùng để mở một luồng scrcpy (đã áp dụng ghi đè riêng của từng máy).</summary>
public sealed record VideoPlan(int MaxSize, int BitRate, int MaxFps);

/// <summary>
/// Cài đặt hình ảnh riêng cho từng điện thoại: một máy yếu (480p) đứng cạnh một máy mạnh (1080p) trong cùng một lưới.
/// Giá trị để trống = dùng cài đặt chung. Hàm thuần để bản tự kiểm tra gọi trực tiếp.
/// </summary>
public static class DeviceVideo
{
    public static VideoPlan Thumb(DeviceMeta? m, Settings s) => new(
        Pick(m?.ThumbMaxSize, s.ThumbMaxSize, 160, 1920),
        Pick(null, s.ThumbBitRate, 30_000, 20_000_000),
        Pick(m?.ThumbFps, s.ThumbFps, 1, 60));

    public static VideoPlan Hq(DeviceMeta? m, Settings s) => new(
        Pick(m?.HqMaxSize, s.HqMaxSize, 320, 2560),
        Pick(m?.HqBitRate, s.HqBitRate, 500_000, 40_000_000),
        Pick(m?.HqFps, s.HqFps, 5, 60));

    /// <summary>Ghi đè hợp lệ (&gt; 0 và trong khoảng) thì dùng, ngược lại dùng giá trị chung.</summary>
    static int Pick(int? over, int global, int min, int max)
    {
        var v = over is int o && o > 0 ? o : global;
        return Math.Clamp(v, min, max);
    }

    public static bool HasOverrides(DeviceMeta? m) =>
        m != null && (m.ThumbFps != null || m.ThumbMaxSize != null || m.HqFps != null || m.HqMaxSize != null || m.HqBitRate != null);
}
