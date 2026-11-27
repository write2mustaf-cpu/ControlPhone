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
        d.AddNote(T("Bản sao lưu ZIP chứa tên máy, số thứ tự, nhóm, cài đặt và lịch sử hội thoại AI — không chứa khoá API."));
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
                // hợp lệ hoá bằng cùng bộ quy tắc dùng khi nạp config.json: tệp hỏng không làm app chạy sai
                s.Extra = null;
                var fixedUp = SettingsSanitizer.Apply(s);
                if (fixedUp.Count > 0) OpLog.Add("Nhập cài đặt: đã sửa " + string.Join(", ", fixedUp));
                Store.State.Settings = s;
                if (imp.Ai != null)
                {
                    imp.Ai.ExtraSystemPrompt ??= "";
                    AiStore.Settings.MaxToolRounds = imp.Ai.MaxToolRounds;
                    AiStore.Settings.RequireApproval = imp.Ai.RequireApproval;
                    AiStore.Settings.ShowToolDetails = imp.Ai.ShowToolDetails;
                    AiStore.Settings.ShowPlan = imp.Ai.ShowPlan;
                    AiStore.Settings.ExtraSystemPrompt = imp.Ai.ExtraSystemPrompt;
                    SettingsSanitizer.Apply(AiStore.Settings);
                    AiStore.Save();
                }
                if (!string.IsNullOrEmpty(imp.Theme)) { Store.Ui.Theme = imp.Theme == "light" ? "light" : "dark"; Theme.Apply(Store.Ui.Theme); }
                if (imp.TileW is double tw) Store.Ui.TileW = Math.Clamp(tw, 110, 520);
                if (!string.IsNullOrEmpty(imp.Sort)) Store.Ui.Sort = imp.Sort;
                if (imp.HideOffline is bool ho) Store.Ui.HideOffline = ho;
                SettingsSanitizer.Apply(Store.Ui);
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
        d.AddAction(T("Sao lưu toàn bộ dữ liệu…"), "Btn", () => { BackupAll(d); return false; }, "shield");
        d.AddAction(T("Khôi phục từ bản sao lưu…"), "Btn", () => { RestoreAll(d); return false; }, "refresh");
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
    /// <summary>ZIP gồm tên máy, số thứ tự, nhóm, cài đặt, tuỳ chọn giao diện và lịch sử hội thoại AI — không có khoá API.</summary>
    void BackupAll(Window owner)
    {
        var sfd = new SaveFileDialog
        {
            Filter = "ControlPhone backup (*.zip)|*.zip",
            FileName = UserBackup.DefaultName(),
            Title = T("Sao lưu toàn bộ dữ liệu"),
        };
        if (sfd.ShowDialog(owner) != true) return;
        try
        {
            var bytes = UserBackup.BuildFromStore();
            File.WriteAllBytes(sfd.FileName, bytes);
            var info = UserBackup.Read(bytes);
            OpLog.Add($"Sao lưu dữ liệu: {info.Manifest.Devices} máy, {info.Manifest.Groups} nhóm, {info.Manifest.Chats} hội thoại → {Path.GetFileName(sfd.FileName)}");
            Ui.Toast(T("Đã sao lưu: {0} máy · {1} nhóm · {2} hội thoại ({3} KB)", info.Manifest.Devices, info.Manifest.Groups, info.Manifest.Chats, bytes.Length / 1024), "ok", 7000);
        }
        catch (Exception e)
        {
            OpLog.Add("Sao lưu dữ liệu thất bại: " + e.Message, error: true);
            Ui.Toast(T("Không tạo được bản sao lưu") + ": " + e.Message, "err", 8000);
        }
    }

    void RestoreAll(Window owner)
    {
        var ofd = new OpenFileDialog { Filter = "ControlPhone backup (*.zip)|*.zip|Tất cả tệp|*.*", Title = T("Khôi phục từ bản sao lưu") };
        if (ofd.ShowDialog(owner) != true) return;
        UserBackup.Info info;
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(ofd.FileName);
            info = UserBackup.Read(bytes);
        }
        catch (Exception e)
        {
            Ui.Toast(T("Tệp sao lưu không hợp lệ") + ": " + e.Message, "err", 8000);
            return;
        }
        var when = info.Manifest.At.ToString("yyyy-MM-dd HH:mm");
        if (!Ui.Confirm(T("Khôi phục dữ liệu"),
                T("Bản sao lưu lúc {0}: {1} máy, {2} nhóm, {3} hội thoại.", when, info.Manifest.Devices, info.Manifest.Groups, info.Manifest.Chats) + "\n\n" +
                T("Tên máy và nhóm trùng số sê-ri sẽ được thay bằng bản trong sao lưu; máy khác giữ nguyên; hội thoại đã có trên máy này không bị ghi đè. Khoá API không có trong tệp nên bạn vẫn cần nhập lại."),
                T("Khôi phục"), true))
            return;
        try
        {
            var doc = info.Doc;
            if (doc.Settings != null)
            {
                doc.Settings.Extra = null;
                SettingsSanitizer.Apply(doc.Settings);
                Store.State.Settings = doc.Settings;
            }
            if (doc.Ai != null)
            {
                var ai = AiStore.Settings;
                ai.MaxToolRounds = doc.Ai.MaxToolRounds;
                ai.RequireApproval = doc.Ai.RequireApproval;
                ai.ShowToolDetails = doc.Ai.ShowToolDetails;
                ai.ShowPlan = doc.Ai.ShowPlan;
                ai.Stream = doc.Ai.Stream;
                ai.ExtraSystemPrompt = doc.Ai.ExtraSystemPrompt ?? "";
                SettingsSanitizer.Apply(ai);
                AiStore.Save();
            }
            if (!string.IsNullOrEmpty(doc.Theme)) { Store.Ui.Theme = doc.Theme == "light" ? "light" : "dark"; Theme.Apply(Store.Ui.Theme); }
            if (doc.TileW is double tw) Store.Ui.TileW = Math.Clamp(tw, 110, 520);
            if (!string.IsNullOrEmpty(doc.Sort)) Store.Ui.Sort = doc.Sort;
            if (doc.HideOffline is bool ho) Store.Ui.HideOffline = ho;
            SettingsSanitizer.Apply(Store.Ui);

            var (devices, groups) = UserBackup.MergeInto(Store.State, doc);
            var chats = UserBackup.ReadChats(bytes);
            var dir = Path.Combine(Store.DataDir, "ai", "chats");
            Directory.CreateDirectory(dir);
            var restored = 0;
            foreach (var id in UserBackup.MissingChats(chats.Keys, id => File.Exists(Path.Combine(dir, id + ".json"))))
            {
                File.WriteAllText(Path.Combine(dir, id + ".json"), chats[id]);
                restored++;
            }

            Store.Save();
            Store.SaveUi();
            manager.ResetStartLimiter();
            manager.RestartAll();
            RenderAll();
            UpdateThemeIcon();
            OpLog.Add($"Khôi phục dữ liệu: +{devices} máy, +{groups} nhóm, +{restored} hội thoại");
            Ui.Toast(T("Đã khôi phục: +{0} máy · +{1} nhóm · +{2} hội thoại", devices, groups, restored), "ok", 7000);
            if (!string.IsNullOrEmpty(doc.Lang) && doc.Lang != I18n.Lang && I18n.Langs.Any(l => l.Code == doc.Lang))
            {
                Store.Ui.Lang = doc.Lang;
                Store.FlushUi();
                App.RestartIn(ModeArgs);
                Close();
            }
        }
        catch (Exception e)
        {
            OpLog.Add("Khôi phục dữ liệu thất bại: " + e.Message, error: true);
            Ui.Toast(T("Khôi phục thất bại") + ": " + e.Message, "err", 8000);
        }
    }
}
