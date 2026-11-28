namespace ControlPhone.Core;

/// <summary>
/// Chia ngân sách giải mã của máy tính cho các luồng đang chạy. Mỗi luồng tốn công CPU theo số điểm ảnh mỗi giây
/// (rộng × cao × fps), nên 10 điện thoại 480p/15 thì ổn nhưng 300 điện thoại cùng mức đó thì không máy nào giải mã kịp —
/// trước đây không có gì giới hạn tổng, mỗi máy chỉ tự lo cho mình.
/// </summary>
public static class LoadBudget
{
    /// <summary>Điểm ảnh/giây mà một lõi CPU giải mã nổi với bộ chuyển YUV→BGRA hiện tại (ước lượng dè dặt).</summary>
    public const int PixelsPerCore = 45_000_000;

    /// <summary>Mức tối thiểu vẫn nhìn được: 240p, 5 fps.</summary>
    public const int MinSize = 240, MinFps = 5;
    /// <summary>Dưới mức này hình bắt đầu vỡ khối ở chuyển động — không hạ nữa.</summary>
    public const int MinBitRate = 120_000;

    /// <summary>Ngân sách cho một luồng khi có <paramref name="streams"/> luồng trên <paramref name="cores"/> lõi.</summary>
    public static long SharePerStream(int streams, int cores) =>
        Math.Max(1_000_000, (long)Math.Max(1, cores) * PixelsPerCore / Math.Max(1, streams));

    /// <summary>Số điểm ảnh/giây của một luồng (giả định khung dọc 16:9, cạnh dài = MaxSize).</summary>
    public static long PixelsPerSecond(int maxSize, int fps)
    {
        var longSide = Math.Clamp(maxSize, 160, 2560);
        var shortSide = longSide * 9 / 16;
        return (long)longSide * shortSide * Math.Clamp(fps, 1, 60);
    }

    /// <summary>
    /// Hạ mức cho vừa ngân sách: cạnh dài về 480 trước, sau đó giảm fps, rồi giảm độ phân giải, và không bao giờ xuống
    /// dưới 240p/5 fps — thà hơi nặng còn hơn không nhìn được.
    /// </summary>
    public static VideoPlan Fit(VideoPlan want, long share)
    {
        var size = Math.Clamp(want.MaxSize, 160, 2560);
        var fps = Math.Clamp(want.MaxFps, 1, 60);
        if (share <= 0) return want;

        // hạ cạnh dài về 480 trước (mức lưới thông thường): giảm nửa số điểm ảnh mà không biến hình thành trình chiếu
        if (size > 480 && PixelsPerSecond(size, fps) > share) size = 480;

        var before = PixelsPerSecond(want.MaxSize, want.MaxFps);   // so với mức người dùng muốn, không phải mức trung gian
        while (PixelsPerSecond(size, fps) > share && fps > MinFps) fps = Math.Max(MinFps, fps - Math.Max(1, fps / 4));
        while (PixelsPerSecond(size, fps) > share && size > MinSize) size = Math.Max(MinSize, size - Math.Max(8, size / 5));
        if (size % 2 != 0) size--;                                  // độ phân giải lẻ làm bộ mã hoá khó chịu
        if (size == want.MaxSize && fps == want.MaxFps) return want;
        // bitrate đi theo số điểm ảnh: hình nhỏ hơn mà vẫn 600 kbps thì phí băng thông (300 máy = 180 Mbps vô ích)
        var rate = (int)Math.Round(want.BitRate * (double)PixelsPerSecond(size, fps) / Math.Max(1, before) / 1000) * 1000;
        return want with { MaxSize = size, MaxFps = fps, BitRate = Math.Clamp(rate, MinBitRate, want.BitRate) };
    }

    /// <summary>Ngân sách hiện tại cho mỗi luồng (0 = không giới hạn). Vòng điều chỉnh cập nhật khi số luồng đổi.</summary>
    public static long ActiveShare { get; private set; }

    public static void SetActive(int streams, int cores)
    {
        ActiveShare = streams <= 0 ? 0 : SharePerStream(streams, cores);
    }

    /// <summary>Áp ngân sách lên một mức hình (dùng cả khi mở luồng mới lẫn khi tính trần cho bộ điều chỉnh).</summary>
    public static VideoPlan Apply(VideoPlan plan) =>
        ActiveShare > 0 && Store.Settings.LoadBudget ? Fit(plan, ActiveShare) : plan;
}
