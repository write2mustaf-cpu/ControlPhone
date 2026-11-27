using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ControlPhone.Ai;
using ControlPhone.Core;
using Microsoft.Win32;

namespace ControlPhone;

/// <summary>
/// Xuất / nhập cài đặt (và giao diện) để chuyển sang máy khác hoặc sao lưu. Tệp xuất KHÔNG chứa khoá API —
/// khoá nằm trong data/ai/ dưới dạng mã hoá DPAPI và không bao giờ rời khỏi máy.
/// </summary>
public partial class MainWindow
{
    sealed class SettingsExport
    {
        public int Version { get; set; } = 1;
        public string App { get; set; } = "ControlPhone";
        public DateTime At { get; set; } = DateTime.Now;
        public Settings? Settings { get; set; }
        public string? Theme { get; set; }
        public string? Lang { get; set; }
        public double? TileW { get; set; }
        public string? Sort { get; set; }
        public bool? HideOffline { get; set; }
        public AiSettings? Ai { get; set; }
    }

    void DlgExportImport()
    {
        var d = new Dlg("Xuất / nhập cài đặt", 620);
        d.AddText("Sao lưu cài đặt để chuyển sang máy khác hoặc khôi phục sau khi cài lại. Tệp xuất chỉ chứa cài đặt và tuỳ chọn giao diện.");
        d.AddNote("Khoá API không nằm trong tệp xuất: khoá được mã hoá bằng Windows DPAPI cho tài khoản Windows hiện tại và không rời khỏi máy. Sau khi nhập cài đặt trên máy mới, hãy nhập lại khoá API.");
        d.AddAction("Đóng");
        d.AddAction("Nhập từ tệp…", "Btn", () =>
        {
            var ofd = new OpenFileDialog { Filter = "ControlPhone settings (*.json)|*.json|Tất cả tệp|*.*", Title = "Nhập cài đặt" };
            if (ofd.ShowDialog(d) != true) return false;
            try
            {
                var text = File.ReadAllText(ofd.FileName);
                var imp = JsonSerializer.Deserialize<SettingsExport>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                          ?? throw new JsonException("tệp rỗng");
                if (imp.Settings == null) throw new JsonException("không có phần cài đặt");
                var s = imp.Settings;
                // giá trị hợp lệ hoá để tệp hỏng không làm app chạy sai
                s.ThumbMaxSize = Math.Clamp(s.ThumbMaxSize, 160, 1920);
                s.ThumbBitRate = Math.Clamp(s.ThumbBitRate, 30_000, 20_000_000);
                s.ThumbFps = Math.Clamp(s.ThumbFps, 1, 60);
                s.HqMaxSize = Math.Clamp(s.HqMaxSize, 320, 2560);
                s.HqBitRate = Math.Clamp(s.HqBitRate, 500_000, 40_000_000);
                s.HqFps = Math.Clamp(s.HqFps, 5, 60);
                s.MaxParallelStart = Math.Clamp(s.MaxParallelStart, 1, 30);
                s.MaxParallelJobs = Math.Clamp(s.MaxParallelJobs, 1, 50);
                if (s.PreferTransport is not ("usb" or "wifi")) s.PreferTransport = "usb";
                if (string.IsNullOrWhiteSpace(s.ScreenshotDir)) s.ScreenshotDir = Path.Combine(Path.GetDirectoryName(Store.DataDir) ?? AppContext.BaseDirectory, "screenshots");
                s.Extra = null;
                Store.State.Settings = s;
                if (imp.Ai != null)
                {
                    imp.Ai.ExtraSystemPrompt ??= "";
                    AiStore.Settings.MaxToolRounds = Math.Clamp(imp.Ai.MaxToolRounds, 1, 20);
                    AiStore.Settings.RequireApproval = imp.Ai.RequireApproval;
                    AiStore.Settings.ShowToolDetails = imp.Ai.ShowToolDetails;
                    AiStore.Settings.ShowPlan = imp.Ai.ShowPlan;
                    AiStore.Settings.ExtraSystemPrompt = imp.Ai.ExtraSystemPrompt;
                    AiStore.Save();
                }
                if (!string.IsNullOrEmpty(imp.Theme)) { Store.Ui.Theme = imp.Theme == "light" ? "light" : "dark"; Theme.Apply(Store.Ui.Theme); }
                if (imp.TileW is double tw) Store.Ui.TileW = Math.Clamp(tw, 110, 520);
                if (!string.IsNullOrEmpty(imp.Sort)) Store.Ui.Sort = imp.Sort;
                if (imp.HideOffline is bool ho) Store.Ui.HideOffline = ho;
                Store.Save();
                Store.SaveUi();
                manager.ResetStartLimiter();
                manager.RestartAll();
                RenderAll();
                UpdateThemeIcon();
                Ui.Toast("Đã nhập cài đặt", "ok");
                if (!string.IsNullOrEmpty(imp.Lang) && imp.Lang != I18n.Lang && I18n.Langs.Any(l => l.Code == imp.Lang))
                {
                    Store.Ui.Lang = imp.Lang; Store.FlushUi();
                    App.RestartIn(ModeArgs);
                    Close();
                }
            }
            catch (Exception e) { Ui.Toast("Tệp cài đặt không hợp lệ: " + e.Message, "err", 8000); }
            return false;
        }, "upload");
        d.AddAction("Xuất ra tệp…", "Primary", () =>
        {
            var sfd = new SaveFileDialog
            {
                Filter = "ControlPhone settings (*.json)|*.json",
                FileName = $"ControlPhone-settings-{DateTime.Now:yyyyMMdd}.json",
                Title = "Xuất cài đặt",
            };
            if (sfd.ShowDialog(d) != true) return false;
            try
            {
                var payload = new SettingsExport
                {
                    Settings = Store.Settings,
                    Theme = Theme.Current,
                    Lang = I18n.Lang,
                    TileW = Store.Ui.TileW,
                    Sort = Store.Ui.Sort,
                    HideOffline = Store.Ui.HideOffline,
                    Ai = AiStore.Settings,
                };
                File.WriteAllText(sfd.FileName, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
                Ui.Toast("Đã xuất cài đặt: " + Path.GetFileName(sfd.FileName), "ok", 5000);
            }
            catch (Exception e) { Ui.Toast("Không ghi được tệp: " + e.Message, "err", 8000); }
            return false;
        }, "download");
        d.ShowDialog();
    }
}
