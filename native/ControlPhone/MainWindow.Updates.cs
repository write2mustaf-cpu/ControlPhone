using System.Globalization;
using ControlPhone.Core;

namespace ControlPhone;

/// <summary>
/// Kiểm tra bản phát hành mới: tự kiểm tra một lần mỗi 24 giờ khi khởi động (có thể tắt), hoặc bấm tay từ menu «⋯».
/// Không tự tải, không tự cài: chỉ mở trang tải trong trình duyệt khi người dùng đồng ý.
/// </summary>
public partial class MainWindow
{
    const string StampFormat = "yyyy-MM-dd HH:mm";

    async void CheckUpdatesAuto()
    {
        try
        {
            if (!Store.Ui.CheckUpdates) return;
            if (DateTime.TryParseExact(Store.Ui.LastUpdateCheck, StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var last)
                && DateTime.Now - last < TimeSpan.FromHours(24)) return;
            await CheckUpdates(manual: false);
        }
        catch (Exception e) { OpLog.Add("Kiểm tra cập nhật (tự động): " + e.Message, error: true); }
    }

    async Task CheckUpdates(bool manual)
    {
        if (manual) Ui.Toast("Đang kiểm tra cập nhật…", "ok", 3000);
        var (info, error) = await Updates.CheckAsync();
        Store.Ui.LastUpdateCheck = DateTime.Now.ToString(StampFormat, CultureInfo.InvariantCulture);
        Store.SaveUi();

        if (info == null)
        {
            OpLog.Add("Kiểm tra cập nhật: " + (error ?? "không có dữ liệu"), error: manual);
            if (manual) Ui.Toast(T("Không kiểm tra được cập nhật: {0}", error ?? ""), "err", 8000);
            return;
        }
        if (!Updates.ShouldNotify(info, Store.Ui.SkipVersion))
        {
            OpLog.Add($"Kiểm tra cập nhật: mới nhất là {info.Tag}, đang dùng {Updates.Current.ToString(3)}");
            if (manual) Ui.Toast(T("Bạn đang dùng bản mới nhất ({0}).", Updates.Current.ToString(3)), "ok", 5000);
            return;
        }
        OpLog.Add($"Có bản mới: {info.Tag} (đang dùng {Updates.Current.ToString(3)})");
        ShowUpdateDialog(info);
    }

    void ShowUpdateDialog(ReleaseInfo info)
    {
        var d = new Dlg("Có bản cập nhật mới", 620);
        d.AddText(T("Bạn đang dùng {0}, bản mới nhất là {1}.", Updates.Current.ToString(3), info.Version.ToString(3)));
        var assets = info.AssetsLine();
        if (assets != "") d.AddNote(assets);
        if (!string.IsNullOrWhiteSpace(info.Notes))
        {
            var body = info.Notes.Length > 2500 ? info.Notes[..2500] + "\n…" : info.Notes;
            var box = new System.Windows.Controls.TextBox
            {
                Text = body,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = System.Windows.TextWrapping.Wrap,
                MaxHeight = 220,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                Margin = new System.Windows.Thickness(0, 0, 0, 10),
            };
            d.Body.Children.Add(box);
        }
        d.AddNote("Ứng dụng không tự tải hay tự cài bản mới: nút bên dưới chỉ mở trang tải trong trình duyệt.");

        d.AddAction("Để sau");
        d.AddAction("Bỏ qua bản này", "Btn", () => { Store.Ui.SkipVersion = info.Tag; Store.SaveUi(); return true; });
        if (info.HasInstaller) d.AddAction("Tải bộ cài", "Btn", () => { OpenLink(info.SetupUrl); return false; }, "download");
        d.AddAction("Mở trang phát hành", "Primary", () => { OpenLink(info.Url); return false; }, "link");
        d.ShowDialog();
    }
}
