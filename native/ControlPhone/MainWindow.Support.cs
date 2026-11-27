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

    /// <summary>Nhật ký riêng của một máy: trạng thái, lỗi luồng và kết quả thao tác — xem, sao chép, mở tệp, xoá.</summary>
    void DlgDeviceLog(PhoneDevice d)
    {
        var dlg = new Dlg(T("Nhật ký máy #{0} · {1}", d.Num, d.DisplayName), 780);
        dlg.AddText("Mọi thay đổi trạng thái của máy này và kết quả các thao tác chạy trên nó được ghi riêng ở đây, và trong tệp bên dưới. Trợ lý AI cũng đọc được nhật ký này bằng công cụ device_log.");
        var path = new TextBlock { Text = DeviceLog.PathFor(d.Id), FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
        path.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        dlg.Body.Children.Add(path);

        var box = new TextBox
        {
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11.5,
            Height = 380,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        void Fill() => box.Text = DeviceLog.Format(d.Id, 200) is { Length: > 0 } t ? t : T("Chưa có ghi nhận nào cho máy này.");
        Fill();
        dlg.Body.Children.Add(box);

        dlg.AddAction("Làm mới", "Btn", () => { Fill(); return false; }, "refresh");
        dlg.AddAction("Sao chép", "Btn", () =>
        {
            try { Clipboard.SetText(box.Text); Ui.Toast(T("Đã sao chép nhật ký máy #{0}", d.Num), "ok"); }
            catch (Exception e) { Ui.Toast(T("Không sao chép được — hãy bôi đen và sao chép thủ công") + ": " + e.Message, "err", 6000); }
            return false;
        }, "copy");
        dlg.AddAction("Mở tệp", "Btn", () =>
        {
            try
            {
                var p = DeviceLog.PathFor(d.Id);
                if (!File.Exists(p)) Ui.Toast(T("Chưa có tệp nhật ký cho máy này"), "err");
                else Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{p}\"") { UseShellExecute = true });
            }
            catch (Exception e) { Ui.Toast(e.Message, "err", 6000); }
            return false;
        }, "folder");
        dlg.AddAction("Xoá nhật ký", "Btn", () =>
        {
            if (!Ui.Confirm(T("Xoá nhật ký máy #{0}", d.Num), T("Xoá toàn bộ nhật ký của máy này (bộ nhớ và tệp)?"), "Xoá", true)) return false;
            DeviceLog.Clear(d.Id);
            DeviceLog.Add(d.Id, "nhật ký đã được xoá");
            Fill();
            return false;
        }, "trash");
        dlg.AddAction("Đóng", "Primary");
        dlg.Show();
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

    // ---------------- phím tắt ----------------
    void InitShortcuts()
    {
        PreviewKeyDown += (_, e) =>
        {
            var action = Shortcuts.Match(e.Key, Keyboard.Modifiers);
            if (action == null) return;
            // trong ô nhập chỉ giữ Esc/F1/Ctrl+F, không cướp phím chữ của người dùng
            if (Keyboard.FocusedElement is TextBox or ComboBox && !Shortcuts.WorksWhileTyping(action)) return;
            RunShortcut(action);
            e.Handled = true;
        };
    }

    /// <summary>Dời ô của một máy về trước/sau trong thứ tự tự xếp (dùng menu ô và Ctrl+Shift+←/→).</summary>
    void MoveDevice(PhoneDevice d, int delta)
    {
        var vis = shown.Select(x => x.Id).ToList();
        int i = vis.IndexOf(d.Id);
        if (i < 0) return;
        int j = i + delta;
        if (j < 0 || j >= vis.Count)
        {
            Ui.Toast(T(delta < 0 ? "Đã ở đầu lưới" : "Đã ở cuối lưới"), "");
            return;
        }
        var full = ManualOrder.Normalize(manager.Devices.Where(x => !x.Pending).Select(x => x.Id), Store.Ui.Order);
        Store.Ui.Order = ManualOrder.MovePast(full, d.Id, vis[j], delta);
        sort = "manual";
        Store.Ui.Sort = sort;
        Store.SaveUi();
        RenderAll();
        foreach (ComboBoxItem it in SortSel.Items) if ((string)it.Tag == sort) SortSel.SelectedItem = it;
        Ui.Toast(T("Đã dời #{0} · thứ tự được lưu lại", d.Num), "ok");
    }

    void RunShortcut(string action)
    {
        var targets = manager.Devices.Where(d => selected.Contains(d.Id)).ToList();
        if (targets.Count == 0) targets = manager.Devices.Where(d => d.IsActive).ToList();
        switch (action)
        {
            case Shortcuts.FocusSearch:
                SearchBox.Focus();
                SearchBox.SelectAll();
                break;
            case Shortcuts.Escape:
                if (SearchBox.Text.Length > 0) { SearchBox.Text = ""; search = ""; RenderAll(); }
                else ClearSelection();
                break;
            case Shortcuts.RefreshInfo:
            {
                var online = manager.Devices.Where(d => d.IsOnline).ToList();
                foreach (var d in online) manager.RefreshInfo(d);
                Ui.Toast(T("Đang cập nhật thông tin {0} máy", online.Count), "ok");
                break;
            }
            case Shortcuts.SelectAll:
                SelectWhere(_ => true);
                break;
            case Shortcuts.InvertSelection:
            {
                var visible = VisibleDevices().Where(d => !d.Pending).Select(d => d.Id).ToList();
                foreach (var id in visible)
                    if (!selected.Remove(id)) selected.Add(id);
                RefreshSelection();
                break;
            }
            case Shortcuts.SoloSelection:
                if (selected.Count == 0) { Ui.Toast(T("Hãy chọn ít nhất một máy trước"), "err"); break; }
                SetSolo([.. selected]);
                break;
            case Shortcuts.ShowAll:
                SetSolo(null);
                break;
            case Shortcuts.PauseToggle:
                SetLive(!PhoneDevice.Live);
                break;
            case Shortcuts.RestartStream:
            {
                var list = targets.Count > 0 ? targets : manager.Devices.Where(d => d.IsOnline).ToList();
                foreach (var d in list) manager.RestartSessions(d);
                Ui.Toast(T("Đang kết nối lại luồng hình của {0} máy", list.Count), "ok");
                break;
            }
            case Shortcuts.AiChat:
                ShowAiChat();
                break;
            case Shortcuts.MoveEarlier:
            case Shortcuts.MoveLater:
            {
                if (targets.Count == 1) MoveDevice(targets[0], action == Shortcuts.MoveEarlier ? -1 : 1);
                else Ui.Toast(T("Dời ô cần chọn đúng một máy"), "");
                break;
            }
            case Shortcuts.Screenshot:
            {
                var d = targets.FirstOrDefault() ?? manager.Devices.FirstOrDefault(x => x.IsOnline);
                if (d == null) { Ui.Toast(T("Không có máy nào để chụp"), "err"); break; }
                _ = SaveShot(d, open: false);
                break;
            }
            case Shortcuts.Help:
                DlgShortcuts();
                break;
        }
    }

    void DlgShortcuts()
    {
        var d = new Dlg(T("Phím tắt"), 620);
        d.AddText(T("Các phím tắt dùng trong cửa sổ chính. Phím tắt không cướp phím chữ khi bạn đang gõ trong ô nhập (trừ Esc, F1, Ctrl+F)."));
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var row = 0;
        foreach (var s in Shortcuts.All)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var keys = new TextBlock
            {
                Text = Shortcuts.Gesture(s),
                FontFamily = new FontFamily("Consolas"),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 3, 12, 3),
            };
            Grid.SetRow(keys, row);
            Grid.SetColumn(keys, 0);
            grid.Children.Add(keys);
            var label = new TextBlock { Text = T(s.Label), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 3) };
            Grid.SetRow(label, row);
            Grid.SetColumn(label, 1);
            grid.Children.Add(label);
            row++;
        }
        d.Body.Children.Add(grid);
        d.AddAction(T("Đóng"), "Primary");
        d.Show();
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
