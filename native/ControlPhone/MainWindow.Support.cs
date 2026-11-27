using System.Diagnostics;
using System.IO;
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

    /// <summary>Danh sách máy cho gói chẩn đoán — số, tên, kiểu kết nối, trạng thái, Android; không có số sê-ri.</summary>
    IEnumerable<string> DiagDeviceLines() => manager.Devices.Select(d =>
        $"#{d.Num}\t{d.Label}\t{(d.ActiveAddr?.Contains(':') == true ? "wifi" : "usb")}\t{(d.IsOnline ? "online" : "offline")}\tAndroid {d.Android}");

    void ExportDiag()
    {
        var sfd = new Microsoft.Win32.SaveFileDialog
        {
            FileName = DiagBundle.DefaultName(),
            Filter = "ZIP (*.zip)|*.zip",
            InitialDirectory = Path.GetDirectoryName(Store.DataDir),
        };
        if (sfd.ShowDialog() != true) return;
        try
        {
            DiagBundle.Build(sfd.FileName, DiagDeviceLines());
            OpLog.Add("Đã xuất gói chẩn đoán: " + sfd.FileName);
            Ui.Toast(T("Đã lưu gói chẩn đoán: {0}", Path.GetFileName(sfd.FileName)), "ok", 7000);
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{sfd.FileName}\"") { UseShellExecute = true }); } catch { }
        }
        catch (Exception e)
        {
            OpLog.Add("Xuất gói chẩn đoán thất bại: " + e.Message, error: true);
            Ui.Toast(T("Không xuất được gói chẩn đoán: {0}", e.Message), "err", 8000);
        }
    }

    /// <summary>Báo cáo kỹ thuật ngắn để người dùng dán vào email/Telegram khi báo lỗi.</summary>
    string ReportText()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("ControlPhone " + (typeof(App).Assembly.GetName().Version?.ToString(3) ?? "?"));
        sb.AppendLine("Windows " + Environment.OSVersion.Version + " · .NET " + Environment.Version + " · " + I18n.Lang);
        sb.AppendLine("Data: " + Store.DataDir);
        try { sb.AppendLine(Diagnostics()); } catch (Exception e) { sb.AppendLine("diagnostics failed: " + e.Message); }
        sb.AppendLine("--- recent operations ---");
        try { sb.AppendLine(OpLog.Format(40)); } catch { }
        return Ai.AiSecret.Redact(sb.ToString().TrimEnd(), null);
    }

    void DlgSupport()
    {
        var d = new Dlg("Liên hệ & hỗ trợ", 700);
        d.AddText("Nếu cần hỗ trợ, gặp lỗi hoặc muốn theo dõi bản cập nhật — hãy liên hệ qua một trong các kênh dưới đây. Mọi liên kết mở được trực tiếp từ trong ứng dụng.");
        if (SafeMode.Active)
            d.AddNote(T("Chế độ an toàn đang bật: hình ảnh nhẹ và không ưu tiên giải mã phần cứng, vì {0} lần khởi động trước không hoàn tất. Đóng ứng dụng rồi mở lại là trở về bình thường.", SafeMode.TriggerFails));
        d.AddNote("Mã Sham Cash chỉ dành cho việc hỗ trợ: người dùng sao chép khi cần; ứng dụng không thực hiện chuyển tiền và không hỏi bất kỳ dữ liệu thanh toán nào.");

        void Card(string title, string subtitle, string value, (string Label, string Icon, Action Run)[] actions)
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = T(title), FontWeight = FontWeights.Bold, FontSize = 13.5, Margin = new Thickness(0, 0, 0, 2) });
            var sub = new TextBlock { Text = T(subtitle), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
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

        Card("Gói chẩn đoán", "Tệp ZIP gồm phiên bản, danh sách máy (không có số sê-ri), nhật ký hoạt động, crash.log và cài đặt. Không chứa khoá API — hãy gửi kèm khi báo lỗi.", "zip",
        [
            ("Xuất gói chẩn đoán…", "download", ExportDiag),
            ("Mở thư mục log", "folder", Crash.OpenLogFolder),
        ]);

        Card("Cập nhật", "Kiểm tra bản phát hành mới trên GitHub — ứng dụng không tự tải hay tự cài.", "v" + Updates.Current.ToString(3),
        [
            ("Kiểm tra ngay", "refresh", () => _ = CheckUpdates(manual: true)),
            ("Mở trang phát hành", "link", () => OpenLink(Updates.RepoUrl + "/releases")),
        ]);
        var chk = d.AddCheck("Tự kiểm tra cập nhật khi khởi động (tối đa 1 lần/24 giờ)", Store.Ui.CheckUpdates);
        chk.Checked += (_, _) => { Store.Ui.CheckUpdates = true; Store.SaveUi(); };
        chk.Unchecked += (_, _) => { Store.Ui.CheckUpdates = false; Store.SaveUi(); };

        Card("Báo lỗi kỹ thuật", "Khi cần hỗ trợ, gửi kèm thông tin chẩn đoán và tệp nhật ký lỗi — chúng không chứa khoá API.", Crash.LogPath,
        [
            ("Mở thư mục nhật ký", "folder", () => Crash.OpenLogFolder()),
            ("Sao chép thông tin chẩn đoán", "copy", () => CopyText("Thông tin chẩn đoán", ReportText())),
        ]);

        d.AddAction("Đóng", "Primary");
        d.ShowDialog();
    }
}
