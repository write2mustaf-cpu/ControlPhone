namespace ControlPhone.Video;

/// <summary>Số đo tức thời của một luồng hình (chỉ đọc từ giao diện, ghi từ luồng giải mã).</summary>
public readonly record struct StreamSnapshot(
    double Fps, double Kbps, double DecodeMs, long Frames, long Dropped, int Width, int Height,
    long Skipped = 0, double AgeMs = 0);

/// <summary>
/// Bộ đếm hiệu năng của một luồng: khung/giây trong cửa sổ 1 giây, kbps từ số byte nhận, thời gian giải mã trung bình
/// (EWMA để một khung chậm không làm nhảy quyết định), khung bị bỏ. Không cấp phát khi chạy, không khoá giao diện.
/// </summary>
public sealed class StreamStats
{
    long frames, dropped, skipped, bytes;
    int width, height;
    double decodeMs;
    long winStart, winFrames, winFpsX100;
    long kbpsX100;
    long lastBytes, lastBytesAt, lastFrameAt;
    bool winStarted, bytesStarted;

    /// <summary>Gọi sau mỗi khung giải mã xong (nowMs để bản tự kiểm tra bơm thời gian giả).</summary>
    public void Frame(int w, int h, double ms, long? nowMs = null)
    {
        var now = nowMs ?? Environment.TickCount64;
        Interlocked.Increment(ref frames);
        width = w; height = h;
        Interlocked.Exchange(ref lastFrameAt, now);
        decodeMs = decodeMs <= 0 ? ms : decodeMs * 0.8 + ms * 0.2;
        if (!winStarted) { winStart = now; winFrames = 0; winStarted = true; }
        winFrames++;
        var span = now - winStart;
        if (span >= 1000)
        {
            winFpsX100 = winFrames * 100_000 / span;
            winStart = now;
            winFrames = 0;
        }
    }

    public void Bytes(int n, long? nowMs = null)
    {
        if (n <= 0) return;
        Interlocked.Add(ref bytes, n);
        var now = nowMs ?? Environment.TickCount64;
        var at = Interlocked.Read(ref lastBytesAt);
        if (!bytesStarted) { bytesStarted = true; Interlocked.Exchange(ref lastBytesAt, now); Interlocked.Exchange(ref lastBytes, bytes); return; }
        var dt = now - at;
        if (dt < 500) return;
        var db = bytes - Interlocked.Read(ref lastBytes);
        // byte trong dt ms → kbit/s; lưu dạng phần trăm để không mất số lẻ khi đọc
        Interlocked.Exchange(ref kbpsX100, db * 800 / dt);
        Interlocked.Exchange(ref lastBytes, bytes);
        Interlocked.Exchange(ref lastBytesAt, now);
    }

    public void Dropped(int n = 1) => Interlocked.Add(ref dropped, n);

    /// <summary>Khung bị bỏ vì chưa có khung khoá (đầu luồng / sau khi mở lại) — KHÔNG phải mất khung.</summary>
    public void Skipped(int n = 1) => Interlocked.Add(ref skipped, n);

    public StreamSnapshot Snapshot(long? nowMs = null)
    {
        var now = nowMs ?? Environment.TickCount64;
        var fps = winFpsX100 / 100.0;
        // cửa sổ đang chạy: ước lượng từ đầu cửa sổ để số liệu không đứng im giữa hai mốc 1 giây
        if (winStarted && winFrames > 0)
        {
            var span = now - winStart;
            if (span >= 250) fps = winFrames * 1000.0 / span;
        }
        var at = Interlocked.Read(ref lastBytesAt);
        // im lặng quá 1,5 giây thì băng thông là 0: luồng chết không được tiếp tục báo 500 kbps
        var kbps = bytesStarted && now - at > 1500 ? 0 : Interlocked.Read(ref kbpsX100) / 100.0;
        var lastFrame = Interlocked.Read(ref lastFrameAt);
        var age = lastFrame == 0 ? 0 : now - lastFrame;
        return new StreamSnapshot(fps, kbps, decodeMs, Interlocked.Read(ref frames), Interlocked.Read(ref dropped),
            width, height, Interlocked.Read(ref skipped), age);
    }
}
