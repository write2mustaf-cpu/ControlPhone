namespace ControlPhone.Core;

/// <summary>
/// Thông tin liên hệ / hỗ trợ chính thức của ControlPhone. Chỉ là thông tin để người dùng mở hoặc sao chép —
/// ứng dụng không thực hiện chuyển tiền và không bao giờ hỏi dữ liệu thanh toán.
/// </summary>
public static class SupportInfo
{
    public const string Email = "mwings740@gmail.com";
    public const string TelegramChannel = "https://t.me/+SiOMr4JAU8thZDFk";
    public const string TelegramGroup = "https://t.me/+ZXt8aHvs5ok2ZjM0";
    public const string Facebook = "https://www.facebook.com/share/1DuqpyXuTa/";
    public const string ShamCash = "963793dbb5e910b81683981b2c8fd9f6";

    public static string AllLinks() =>
        $"{TelegramChannel}\n{TelegramGroup}\n{Facebook}\n{Email}\nSham Cash: {ShamCash}";
}
