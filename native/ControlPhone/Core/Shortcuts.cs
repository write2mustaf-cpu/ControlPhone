using System.Windows.Input;

namespace ControlPhone.Core;

/// <summary>Một phím tắt của cửa sổ chính: tổ hợp phím, mã hành động và nhãn hiển thị trong bảng trợ giúp.</summary>
public sealed record Shortcut(Key Key, ModifierKeys Mods, string Action, string Label);

/// <summary>
/// Bảng phím tắt của ControlPhone. Hàm <see cref="Match"/> thuần nên bản tự kiểm tra so khớp được từng tổ hợp mà
/// không cần bấm phím thật.
/// </summary>
public static class Shortcuts
{
    public const string FocusSearch = "focus-search";
    public const string Escape = "escape";
    public const string RefreshInfo = "refresh-info";
    public const string SelectAll = "select-all";
    public const string InvertSelection = "invert-selection";
    public const string SoloSelection = "solo-selection";
    public const string ShowAll = "show-all";
    public const string PauseToggle = "pause-toggle";
    public const string RestartStream = "restart-stream";
    public const string AiChat = "ai-chat";
    public const string Screenshot = "screenshot";
    public const string Help = "help";
    public const string MoveEarlier = "move-earlier";
    public const string MoveLater = "move-later";

    public static readonly Shortcut[] All =
    [
        new(Key.F, ModifierKeys.Control, FocusSearch, "Tìm máy…"),
        new(Key.Escape, ModifierKeys.None, Escape, "Xoá ô tìm kiếm / bỏ chọn"),
        new(Key.F5, ModifierKeys.None, RefreshInfo, "Cập nhật thông tin các máy"),
        new(Key.A, ModifierKeys.Control, SelectAll, "Chọn tất cả máy đang hiện"),
        new(Key.I, ModifierKeys.Control, InvertSelection, "Đảo lựa chọn"),
        new(Key.L, ModifierKeys.Control, SoloSelection, "Chỉ hiện máy đang chọn"),
        new(Key.L, ModifierKeys.Control | ModifierKeys.Shift, ShowAll, "Hiện lại tất cả máy"),
        new(Key.P, ModifierKeys.Control, PauseToggle, "Tạm dừng / tiếp tục xem"),
        new(Key.R, ModifierKeys.Control, RestartStream, "Kết nối lại luồng hình"),
        new(Key.A, ModifierKeys.Control | ModifierKeys.Shift, AiChat, "Mở trợ lý AI"),
        new(Key.S, ModifierKeys.Control | ModifierKeys.Shift, Screenshot, "Chụp màn hình máy đang chọn"),
        new(Key.Left, ModifierKeys.Control | ModifierKeys.Shift, MoveEarlier, "Dời ô đang chọn về trước"),
        new(Key.Right, ModifierKeys.Control | ModifierKeys.Shift, MoveLater, "Dời ô đang chọn ra sau"),
        new(Key.F1, ModifierKeys.None, Help, "Danh sách phím tắt"),
    ];

    /// <summary>Phím tắt dùng được khi con trỏ đang ở trong ô nhập (không cướp phím chữ).</summary>
    public static bool WorksWhileTyping(string action) => action is Escape or Help or FocusSearch;

    public static string? Match(Key key, ModifierKeys mods)
    {
        mods &= ~ModifierKeys.Windows;
        foreach (var s in All)
            if (s.Key == key && s.Mods == mods) return s.Action;
        return null;
    }

    public static string Gesture(Shortcut s)
    {
        var parts = new List<string>();
        if (s.Mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (s.Mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (s.Mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        parts.Add(s.Key switch
        {
            Key.Escape => "Esc",
            Key.Return => "Enter",
            _ => s.Key.ToString(),
        });
        return string.Join("+", parts);
    }
}
