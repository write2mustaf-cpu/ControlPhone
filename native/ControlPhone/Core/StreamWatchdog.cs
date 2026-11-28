namespace ControlPhone.Core;

/// <summary>Việc cần làm khi một ô lưới không vẽ được khung nào.</summary>
public enum RecoveryStep
{
    /// <summary>Chưa tới ngưỡng, hoặc đang trong thời gian chờ: không làm gì.</summary>
    Wait,
    /// <summary>Khung chảy bình thường.</summary>
    None,
    /// <summary>Xin keyframe (thường đủ khi chỉ lỡ một keyframe).</summary>
    KeyFrame,
    /// <summary>Đánh thức bộ giải mã rồi xin keyframe (bộ giải mã đã ngủ vì luồng im).</summary>
    Reset,
    /// <summary>Dựng lại cả phiên: keyframe không tới nghĩa là phiên còn sống nhưng không còn gửi gì.</summary>
    Restart,
}

/// <summary>
/// Chính sách tự phục hồi cho luồng lưới, tách khỏi đồng hồ và khỏi UI để kiểm thử được. Trước đây bộ theo dõi chỉ xin
/// keyframe 2,5 giây/lần (tối đa 3 lần) rồi <b>đầu hàng</b>: ô vẫn hiện khung cũ, chấm "online" vẫn xanh, và người dùng
/// tưởng máy đang đứng yên trong khi thực ra luồng đã chết — phải bấm mở lại từng máy. Giờ lần thứ tư thì dựng lại phiên,
/// nhưng không sớm hơn một phút một lần để một máy hỏng không bị đập đi đập lại.
/// </summary>
public static class StreamWatchdog
{
    /// <summary>Không có khung mới bao lâu thì bắt đầu xin keyframe.</summary>
    public const long KeyAfterMs = 2500;

    /// <summary>Không có khung mới bao lâu thì đánh thức bộ giải mã.</summary>
    public const long ResetAfterMs = 6000;

    /// <summary>Số lần xin keyframe/đánh thức trước khi dựng lại phiên.</summary>
    public const int MaxAttempts = 3;

    /// <summary>Hai lần dựng lại phiên của cùng một máy cách nhau ít nhất chừng này.</summary>
    public const long RestartCooldownMs = 60_000;

    public static RecoveryStep Decide(bool online, bool video, long msSinceProgress, int attempts, long msSinceRestart)
    {
        if (!online || !video) return RecoveryStep.Wait;
        if (msSinceProgress < KeyAfterMs) return RecoveryStep.None;
        if (attempts <= 0) return RecoveryStep.KeyFrame;
        if (msSinceProgress < ResetAfterMs) return RecoveryStep.Wait;
        if (attempts < MaxAttempts) return RecoveryStep.Reset;
        return msSinceRestart >= RestartCooldownMs ? RecoveryStep.Restart : RecoveryStep.Wait;
    }
}
