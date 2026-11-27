using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ControlPhone.Core;

namespace ControlPhone;

/// <summary>
/// Mục «Liên hệ & hỗ trợ»: Email, kênh và nhóm Telegram, trang Facebook, mã hỗ trợ Sham Cash —
/// kèm nút mở và sao chép hoạt động thật, có thông báo khi sao chép thành công. Không thực hiện chuyển tiền.
/// </summary>
public partial class MainWindow
{
    static void CopyText(string label, string value)
    {
        try
        {
            Clipboard.SetText(value);
            Ui.Toast(T("Đã sao chép {0}", T(label)), "ok");
        }
        catch (Exception e)
        {
            Ui.Toast(T("Không sao chép được — hãy bôi đen và sao chép thủ công") + ": " + e.Message, "err", 6000);
        }
    }

    static void OpenLink(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception e) { Ui.Toast(T("Không mở được liên kết") + ": " + e.Message, "err", 6000); }
    }

    void DlgSupport()
    {
        var d = new Dlg("Liên hệ & hỗ trợ", 700);
        d.AddText("Nếu cần hỗ trợ, gặp lỗi hoặc muốn theo dõi bản cập nhật — hãy liên hệ qua một trong các kênh dưới đây. Mọi liên kết mở được trực tiếp từ trong ứng dụng.");
        d.AddNote("Mã Sham Cash chỉ dành cho việc hỗ trợ: người dùng sao chép khi cần; ứng dụng không thực hiện chuyển tiền và không hỏi bất kỳ dữ liệu thanh toán nào.");

        void Card(string title, string subtitle, string value, (string Label, string Icon, Action Run)[] actions)
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, FontSize = 13.5, Margin = new Thickness(0, 0, 0, 2) });
            var sub = new TextBlock { Text = subtitle, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
            sub.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            sp.Children.Add(sub);
            var val = new TextBox { Text = value, IsReadOnly = true, FontFamily = new FontFamily("Consolas"), Margin = new Thickness(0, 0, 0, 8) };
            I18n.ForceLtr(val);
            sp.Children.Add(val);
            var row = new WrapPanel();
            bool first = true;
            foreach (var (label, icon, run) in actions)
            {
                var b = Ui.Btn(label, icon, first ? "Primary" : "Btn", (_, _) => run());
                b.Margin = new Thickness(0, 0, 6, 4);
                row.Children.Add(b);
                first = false;
            }
            sp.Children.Add(row);
            var border = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(14, 11, 14, 11), Margin = new Thickness(0, 0, 0, 8), Child = sp, BorderThickness = new Thickness(1) };
            border.SetResourceReference(Border.BackgroundProperty, "Bg3");
            border.SetResourceReference(Border.BorderBrushProperty, "Line");
            d.Body.Children.Add(border);
        }

        Card("Email", "Hỗ trợ kỹ thuật, câu hỏi và góp ý.", SupportInfo.Email,
        [
            ("Gửi email hỗ trợ", "link", () => OpenLink("mailto:" + SupportInfo.Email + "?subject=ControlPhone%20support")),
            ("Sao chép email", "copy", () => CopyText("Email", SupportInfo.Email)),
        ]);

        Card("Kênh Telegram", "Cập nhật và bản phát hành mới.", SupportInfo.TelegramChannel,
        [
            ("Mở kênh", "link", () => OpenLink(SupportInfo.TelegramChannel)),
            ("Sao chép liên kết", "copy", () => CopyText("Liên kết kênh", SupportInfo.TelegramChannel)),
        ]);

        Card("Nhóm Telegram", "Hỏi đáp và trợ giúp từ người dùng khác.", SupportInfo.TelegramGroup,
        [
            ("Mở nhóm", "link", () => OpenLink(SupportInfo.TelegramGroup)),
            ("Sao chép liên kết", "copy", () => CopyText("Liên kết nhóm", SupportInfo.TelegramGroup)),
        ]);

        Card("Trang Facebook", "Tin tức và thảo luận.", SupportInfo.Facebook,
        [
            ("Mở trang", "link", () => OpenLink(SupportInfo.Facebook)),
            ("Sao chép liên kết", "copy", () => CopyText("Liên kết Facebook", SupportInfo.Facebook)),
        ]);

        Card("Mã hỗ trợ Sham Cash", "Chỉ sao chép khi bạn muốn ủng hộ dự án. Ứng dụng không thực hiện chuyển tiền và không hỏi dữ liệu thanh toán.", SupportInfo.ShamCash,
        [
            ("Sao chép mã", "copy", () => CopyText("Mã Sham Cash", SupportInfo.ShamCash)),
        ]);

        var all = Ui.Btn("Sao chép tất cả liên kết", "copy", "Btn", (_, _) => CopyText("Tất cả liên kết", SupportInfo.AllLinks()));
        all.Margin = new Thickness(0, 0, 0, 8);
        d.Body.Children.Add(all);

        d.AddAction("Đóng", "Primary");
        d.ShowDialog();
    }
}
