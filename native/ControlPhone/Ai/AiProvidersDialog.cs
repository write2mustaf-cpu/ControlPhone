using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ControlPhone.Core;

namespace ControlPhone.Ai;

/// <summary>
/// Quản lý nhà cung cấp AI: thêm / sửa / xoá, chọn model, kiểm tra khoá, đặt mặc định, giới hạn độ dài và thời gian chờ.
/// Khoá API do người dùng tự nhập, chỉ lưu dạng mã hoá DPAPI, luôn hiện dưới dạng che và không bao giờ ghi nhật ký.
/// </summary>
public static class AiProvidersDialog
{
    public static void Show(Window? owner = null)
    {
        var d = new Dlg("Nhà cung cấp AI", 780);
        d.AddNote("Mỗi người dùng tự nhập khoá API của mình. Khoá được mã hoá cục bộ bằng Windows DPAPI cho tài khoản Windows hiện tại, không ghi vào nhật ký và không gửi tới đâu ngoài nhà cung cấp bạn chọn. Không có khoá nào được nhúng trong chương trình.");
        var list = new StackPanel();
        var listHost = new Border { Child = new ScrollViewer { Content = list, MaxHeight = 330, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, Padding = new Thickness(2) };
        d.Body.Children.Add(listHost);

        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 6) };
        var add = Ui.Btn("Thêm nhà cung cấp", "plus", "Primary");
        add.Click += (_, _) => ShowMenu(add);
        buttons.Children.Add(add);
        var settingsBtn = Ui.Btn("Tuỳ chọn trợ lý", "settings", "Btn", (_, _) => EditSettings(d));
        buttons.Children.Add(settingsBtn);
        var wipe = Ui.Btn("Xoá toàn bộ dữ liệu AI", "trash", "Danger");
        wipe.Margin = new Thickness(6, 0, 0, 0);
        wipe.Click += (_, _) =>
        {
            if (!Ui.Confirm("Xoá toàn bộ dữ liệu AI", "Sẽ xoá vĩnh viễn mọi nhà cung cấp, khoá đã mã hoá và lịch sử hội thoại. Tiếp tục?", "Xoá tất cả", true)) return;
            AiStore.DeleteAll();
            Render();
            Ui.Toast("Đã xoá toàn bộ dữ liệu AI", "ok");
        };
        buttons.Children.Add(wipe);
        d.Body.Children.Add(buttons);

        void Render()
        {
            list.Children.Clear();
            if (AiStore.Providers.Count == 0)
            {
                var empty = new TextBlock { Text = "Chưa có nhà cung cấp nào. Hãy thêm Google Gemini, OpenAI, Anthropic hoặc dịch vụ tương thích OpenAI rồi nhập khoá của bạn.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 10, 8, 10) };
                empty.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
                list.Children.Add(empty);
                return;
            }
            foreach (var p in AiStore.Providers.ToList())
            {
                var row = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 6) };
                row.SetResourceReference(Border.BackgroundProperty, "Bg3");
                var dp = new DockPanel();
                var acts = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var edit = Ui.Btn("Sửa", "type", "Btn", (_, _) => Edit(p, d, Render));
                var test = Ui.Btn("Kiểm tra", "zap", "Btn");
                test.Margin = new Thickness(6, 0, 0, 0);
                test.Click += async (_, _) =>
                {
                    test.IsEnabled = false;
                    try
                    {
                        var key = AiStore.KeyOf(p);
                        var (ok, msg) = await AiClient.TestAsync(p, key, CancellationToken.None);
                        if (ok) { p.LastOk = DateTime.Now; p.LastError = ""; Ui.Toast("Kết nối thành công: " + msg, "ok", 6000); }
                        else { p.LastError = msg; Ui.Toast("Kết nối thất bại: " + msg, "err", 9000); }
                        AiStore.Upsert(p, p.IsDefault);
                        Render();
                    }
                    finally { test.IsEnabled = true; }
                };
                acts.Children.Add(edit);
                acts.Children.Add(test);
                if (!p.IsDefault)
                {
                    var def = Ui.Btn("Mặc định", "check", "Btn");
                    def.Margin = new Thickness(6, 0, 0, 0);
                    def.Click += (_, _) => { AiStore.SetDefault(p.Id); Render(); };
                    acts.Children.Add(def);
                }
                var del = Ui.Btn("", "trash", "IconBtn");
                del.Margin = new Thickness(6, 0, 0, 0);
                del.Click += (_, _) =>
                {
                    if (!Ui.Confirm("Xoá nhà cung cấp", $"Xoá \"{p.Display}\" và khoá đã mã hoá của nó?", "Xoá", true)) return;
                    AiStore.Delete(p.Id);
                    Render();
                };
                acts.Children.Add(del);
                DockPanel.SetDock(acts, Dock.Right);
                dp.Children.Add(acts);

                var sp = new StackPanel();
                var title = new TextBlock { Text = p.Display + (p.IsDefault ? "  ·  ★ " + I18n.T("Mặc định") : ""), FontWeight = FontWeights.Bold, FontSize = 13 };
                sp.Children.Add(title);
                var meta = new TextBlock
                {
                    Text = $"{AiPresets.KindLabel(p.Kind)} · {p.Model} · {(p.HasKey ? I18n.T("Đã lưu khoá (mã hoá)") : I18n.T("Chưa có khoá"))}" +
                           $"{(p.LastOk != null ? " · " + I18n.T("Lần thành công cuối") + " " + p.LastOk.Value.ToString("dd/MM HH:mm") : "")}" +
                           (string.IsNullOrEmpty(p.LastError) ? "" : " · " + I18n.T("Lỗi cuối") + ": " + p.LastError),
                    FontSize = 11.5,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0),
                };
                meta.SetResourceReference(TextBlock.ForegroundProperty, string.IsNullOrEmpty(p.LastError) ? "Muted" : "Bad");
                sp.Children.Add(meta);
                dp.Children.Add(sp);
                row.Child = dp;
                list.Children.Add(row);
            }
        }

        void ShowMenu(FrameworkElement anchor)
        {
            var cm = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            foreach (var preset in AiPresets.All)
            {
                var mi = new MenuItem { Header = preset.Name };
                mi.Click += (_, _) => Edit(NewFromPreset(preset), d, Render);
                cm.Items.Add(mi);
            }
            cm.IsOpen = true;
        }

        Render();
        d.AddAction("Đóng", "Primary");
        if (owner != null) d.Owner = owner;
        d.ShowDialog();
    }

    static AiProvider NewFromPreset(AiPresets.Preset p) => new()
    {
        Name = p.Name,
        Kind = p.Kind,
        BaseUrl = p.BaseUrl,
        Model = p.Model,
        IsDefault = AiStore.Providers.Count == 0,
    };

    static void Edit(AiProvider provider, Window parent, Action refresh)
    {
        var p = provider.Clone();
        var d = new Dlg(p.Name == "" ? "Nhà cung cấp mới" : "Sửa nhà cung cấp", 640) { Owner = parent };
        var name = d.AddInput("Tên", p.Name, "vd. Gemini cá nhân");
        var kindItems = new List<(string Value, string Text)>();
        foreach (var k in new[] { AiProviderKind.Gemini, AiProviderKind.OpenAi, AiProviderKind.Anthropic, AiProviderKind.OpenAiCompatible })
            kindItems.Add((((int)k).ToString(), AiPresets.KindLabel(k)));
        var kind = d.AddCombo("Loại dịch vụ", kindItems, ((int)p.Kind).ToString());
        var baseUrl = d.AddInput("Địa chỉ API (Base URL)", p.BaseUrl, "https://…");
        var model = d.AddInput("Model", p.Model, "gemini-2.5-flash · gpt-4o-mini · claude-sonnet-4-5 …");
        I18n.ForceLtr(baseUrl);
        I18n.ForceLtr(model);

        var keyRow = new StackPanel();
        var keyBox = new PasswordBox { Tag = I18n.T("Để trống để giữ khoá hiện tại") };
        keyRow.Children.Add(keyBox);
        var keyNote = new TextBlock
        {
            Text = p.HasKey ? I18n.T("Đã có khoá được lưu (mã hoá). Nhập khoá mới để thay thế.") : I18n.T("Chưa có khoá."),
            FontSize = 11.5,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        keyNote.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        keyRow.Children.Add(keyNote);
        d.Body.Children.Add(d.Field("Khoá API", keyRow));

        var numRow = new Grid();
        for (int i = 0; i < 3; i++) numRow.ColumnDefinitions.Add(new ColumnDefinition());
        var maxTok = new TextBox { Text = p.MaxTokens.ToString() };
        var temp = new TextBox { Text = p.Temperature.ToString("0.##") };
        var timeout = new TextBox { Text = p.TimeoutSeconds.ToString() };
        var f1 = d.Field("Độ dài tối đa của trả lời (tokens)", maxTok);
        var f2 = d.Field("Độ sáng tạo (temperature)", temp);
        var f3 = d.Field("Thời gian chờ (giây)", timeout);
        f1.Margin = new Thickness(0, 0, 8, 10);
        f2.Margin = new Thickness(0, 0, 8, 10);
        Grid.SetColumn(f1, 0); Grid.SetColumn(f2, 1); Grid.SetColumn(f3, 2);
        numRow.Children.Add(f1); numRow.Children.Add(f2); numRow.Children.Add(f3);
        d.Body.Children.Add(numRow);

        var statusLine = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11.8, Margin = new Thickness(0, 4, 0, 0) };
        statusLine.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        d.Body.Children.Add(statusLine);

        var row = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var modelsBtn = Ui.Btn("Lấy danh sách model", "refresh", "Btn");
        modelsBtn.Click += async (_, _) =>
        {
            Apply();
            statusLine.Text = I18n.T("Đang tải danh sách model…");
            var (ok, models, msg) = await AiClient.ListModelsAsync(p, AiStore.KeyOf(p), CancellationToken.None);
            statusLine.Text = msg;
            if (!ok || models.Count == 0) return;
            var cm = new ContextMenu { PlacementTarget = model, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            foreach (var m in models.Take(300))
            {
                var mi = new MenuItem { Header = m };
                mi.Click += (_, _) => model.Text = m;
                cm.Items.Add(mi);
            }
            cm.IsOpen = true;
        };
        row.Children.Add(modelsBtn);
        var testBtn = Ui.Btn("Kiểm tra kết nối", "zap", "Primary");
        testBtn.Margin = new Thickness(6, 0, 0, 0);
        testBtn.Click += async (_, _) =>
        {
            Apply();
            if (string.IsNullOrWhiteSpace(p.Model)) { statusLine.Text = I18n.T("Hãy nhập tên model trước."); return; }
            var key = keyBox.Password.Trim() is { Length: > 0 } typed ? typed : AiStore.KeyOf(p);
            testBtn.IsEnabled = false;
            statusLine.Text = I18n.T("Đang kiểm tra…");
            try
            {
                var (ok, msg) = await AiClient.TestAsync(p, key, CancellationToken.None);
                statusLine.Text = (ok ? "✓ " : "✗ ") + msg;
                statusLine.SetResourceReference(TextBlock.ForegroundProperty, ok ? "Ok" : "Bad");
                p.LastOk = ok ? DateTime.Now : p.LastOk;
                p.LastError = ok ? "" : msg;
            }
            finally { testBtn.IsEnabled = true; }
        };
        row.Children.Add(testBtn);
        d.Body.Children.Add(row);

        void Apply()
        {
            p.Name = name.Text.Trim();
            p.Kind = (AiProviderKind)int.Parse((string)((ComboBoxItem)kind.SelectedItem).Tag);
            p.BaseUrl = baseUrl.Text.Trim();
            p.Model = model.Text.Trim();
            p.MaxTokens = int.TryParse(maxTok.Text, out var mt) ? Math.Clamp(mt, 64, 32000) : 1200;
            p.Temperature = double.TryParse(temp.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var tp) ? Math.Clamp(tp, 0, 2) : 0.4;
            p.TimeoutSeconds = int.TryParse(timeout.Text, out var ts) ? Math.Clamp(ts, 10, 900) : 120;
        }

        d.AddAction("Huỷ");
        d.AddAction("Lưu", "Primary", () =>
        {
            Apply();
            if (string.IsNullOrWhiteSpace(p.Name)) p.Name = AiPresets.KindLabel(p.Kind);
            if (string.IsNullOrWhiteSpace(p.BaseUrl)) { Ui.Toast("Hãy nhập địa chỉ API (Base URL)", "err"); return false; }
            var typed = keyBox.Password.Trim();
            if (typed != "") AiStore.SetKey(p, typed);
            else if (!p.HasKey && p.Kind != AiProviderKind.OpenAiCompatible) { Ui.Toast("Hãy nhập khoá API cho nhà cung cấp này", "err"); return false; }
            AiStore.Upsert(p, p.IsDefault || AiStore.Providers.Count == 0);
            Ui.Toast("Đã lưu nhà cung cấp", "ok");
            refresh();
            return true;
        });
        d.Loaded += (_, _) => name.Focus();
        d.ShowDialog();
    }

    static void EditSettings(Window parent)
    {
        var s = AiStore.Settings;
        var d = new Dlg("Tuỳ chọn trợ lý", 620) { Owner = parent };
        var rounds = new TextBox { Text = s.MaxToolRounds.ToString() };
        d.Body.Children.Add(d.Field("Số bước công cụ tối đa cho mỗi yêu cầu", rounds));
        var approval = d.AddCheck("Hỏi tôi trước các thao tác nhạy cảm (cài đặt, gỡ, shell, gửi văn bản…)", s.RequireApproval);
        var details = d.AddCheck("Hiện chi tiết kết quả công cụ trong hội thoại", s.ShowToolDetails);
        var plan = d.AddCheck("Hiện kế hoạch thao tác trước khi thực hiện", s.ShowPlan);
        var extra = d.AddInput("Hướng dẫn thêm cho trợ lý (tuỳ chọn)", s.ExtraSystemPrompt, "vd. Luôn trả lời bằng tiếng Việt, nêu rõ số máy");
        extra.AcceptsReturn = true;
        extra.TextWrapping = TextWrapping.Wrap;
        extra.Height = 80;
        d.AddNote("Các tuỳ chọn này áp dụng cho mọi hội thoại. Khoá API không lưu ở đây — xem danh sách nhà cung cấp.");
        d.AddAction("Huỷ");
        d.AddAction("Lưu", "Primary", () =>
        {
            s.MaxToolRounds = int.TryParse(rounds.Text, out var r) ? Math.Clamp(r, 1, 20) : 8;
            s.RequireApproval = approval.IsChecked == true;
            s.ShowToolDetails = details.IsChecked == true;
            s.ShowPlan = plan.IsChecked == true;
            s.ExtraSystemPrompt = extra.Text.Trim();
            AiStore.Save();
            Ui.Toast("Đã lưu tuỳ chọn trợ lý", "ok");
            return true;
        });
        d.ShowDialog();
    }
}
