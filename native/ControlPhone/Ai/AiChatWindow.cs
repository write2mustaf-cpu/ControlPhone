using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ControlPhone.Core;

namespace ControlPhone.Ai;

/// <summary>
/// Khung chat của trợ lý AI: chọn nhà cung cấp / model, lịch sử hội thoại, thẻ công cụ đang chạy (có nút dừng),
/// hỏi duyệt trước thao tác nhạy cảm, và toàn bộ nội dung được bố trí theo hướng của ngôn ngữ đang dùng (RTL/العربية).
/// </summary>
public sealed class AiChatWindow : Window
{
    readonly AiToolHost tools;
    readonly AiAgent agent;
    readonly StackPanel messages = new();
    readonly ScrollViewer scroller;
    readonly TextBox input;
    readonly Button sendBtn;
    readonly Button stopBtn;
    readonly ComboBox providerSel = new() { Width = 190, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBox modelBox = new() { Width = 180, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock status = new() { FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly ListBox chatList = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    readonly Dictionary<string, Border> toolCards = [];
    readonly Dictionary<string, TextBlock> toolTexts = [];
    AiChat? chat;
    CancellationTokenSource? cts;
    bool busy;

    public AiChatWindow(AiToolHost tools)
    {
        this.tools = tools;
        Title = "ControlPhone AI — " + I18n.T("Trợ lý thông minh");
        Width = 980;
        Height = 720;
        MinWidth = 720;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "Bg2");
        SetResourceReference(ForegroundProperty, "Text");
        FontSize = 12.5;
        FlowDirection = I18n.FlowDirection;
        Ui.DarkTitleBar(this);

        // ---------- cột trái: danh sách hội thoại ----------
        var left = new DockPanel { Width = 232, Margin = new Thickness(0, 0, 1, 0) };
        left.SetResourceReference(Border.BackgroundProperty, "Bg2");
        var newBtn = Ui.Btn("Hội thoại mới", "plus", "Primary", (_, _) => NewChat());
        newBtn.Margin = new Thickness(8, 8, 8, 6);
        DockPanel.SetDock(newBtn, Dock.Top);
        left.Children.Add(newBtn);
        var delBtn = Ui.Btn("Xoá hội thoại", "trash", "Btn", (_, _) => DeleteChat());
        delBtn.Margin = new Thickness(8, 6, 8, 8);
        DockPanel.SetDock(delBtn, Dock.Bottom);
        left.Children.Add(delBtn);
        chatList.SelectionChanged += (_, _) => { if (!busy && chatList.SelectedItem is ChatRow r) LoadChat(r.Id); };
        var listScroll = new ScrollViewer { Content = chatList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        left.Children.Add(listScroll);
        var leftBorder = new Border { Child = left };
        leftBorder.SetResourceReference(Border.BorderBrushProperty, "Line");
        leftBorder.BorderThickness = new Thickness(0, 0, 1, 0);

        // ---------- cột phải: chat ----------
        var header = new DockPanel { Margin = new Thickness(10, 8, 10, 8) };
        var title = new TextBlock { Text = I18n.T("Trợ lý ControlPhone"), FontSize = 14.5, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        header.Children.Add(title);
        providerSel.SelectionChanged += (_, _) => ProviderChanged();
        header.Children.Add(providerSel);
        modelBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) SaveModel(); };
        modelBox.LostFocus += (_, _) => SaveModel();
        header.Children.Add(modelBox);
        var reload = new Button { Content = Icons.Get("refresh"), ToolTip = I18n.T("Nạp lại danh sách model"), Width = 32, Height = 30, Margin = new Thickness(0, 0, 6, 0) };
        reload.SetResourceReference(StyleProperty, "IconBtn");
        reload.Click += async (_, _) => await LoadModelsAsync();
        header.Children.Add(reload);
        var cfg = Ui.Btn("", "settings", "IconBtn", (_, _) => AiProvidersDialog.Show(this));
        cfg.Margin = new Thickness(0, 0, 6, 0);
        cfg.ToolTip = I18n.T("Nhà cung cấp & khoá API");
        header.Children.Add(cfg);
        var right = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        right.Children.Add(header);

        var headerBorder = new Border { Child = header };
        headerBorder.SetResourceReference(Border.BorderBrushProperty, "Line");
        headerBorder.BorderThickness = new Thickness(0, 0, 0, 1);

        scroller = new ScrollViewer { Content = messages, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(14, 10, 14, 10) };
        messages.Margin = new Thickness(0);

        // ---------- ô soạn ----------
        input = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 44,
            MaxHeight = 150,
            VerticalContentAlignment = VerticalAlignment.Top,
            Tag = I18n.T("Hỏi trợ lý… (Enter: gửi · Shift+Enter: xuống dòng)"),
            Margin = new Thickness(0, 0, 8, 0),
        };
        input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) { e.Handled = true; _ = SendAsync(); }
        };
        sendBtn = Ui.Btn("Gửi", "zap", "Primary", (_, _) => _ = SendAsync());
        sendBtn.MinWidth = 92;
        stopBtn = Ui.Btn("Dừng", "stop", "Btn", (_, _) => cts?.Cancel());
        stopBtn.MinWidth = 84;
        stopBtn.IsEnabled = false;
        stopBtn.Margin = new Thickness(8, 0, 0, 0);
        var composerRow = new DockPanel { Margin = new Thickness(10, 8, 10, 4) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
        buttons.Children.Add(sendBtn);
        buttons.Children.Add(stopBtn);
        DockPanel.SetDock(buttons, Dock.Right);
        composerRow.Children.Add(buttons);
        composerRow.Children.Add(input);

        status.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        status.Margin = new Thickness(12, 0, 12, 8);
        var bottom = new StackPanel();
        bottom.Children.Add(composerRow);
        bottom.Children.Add(status);
        DockPanel.SetDock(bottom, Dock.Bottom);
        right.Children.Add(bottom);
        right.Children.Add(scroller);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(leftBorder, 0);
        Grid.SetColumn(right, 1);
        grid.Children.Add(leftBorder);
        grid.Children.Add(right);
        Content = grid;

        agent = new AiAgent(tools) { UiPost = a => Dispatcher.BeginInvoke(a) };
        agent.Event += OnAgentEvent;
        agent.Approve = ApproveAsync;

        Loaded += (_, _) =>
        {
            ReloadProviders();
            ReloadChatList();
            if (chat == null) NewChat();
            input.Focus();
            if (AiStore.Providers.Count == 0) ShowEmptyState();
        };
        Closing += (_, _) => { cts?.Cancel(); if (chat != null) AiStore.SaveChat(chat); };
    }

    // ---------------- danh sách ----------------
    sealed record ChatRow(string Id, string Label);

    void ReloadChatList()
    {
        var keep = chat?.Id;
        chatList.Items.Clear();
        foreach (var c in AiStore.State.Chats.OrderByDescending(x => x.Updated))
            chatList.Items.Add(new ChatRow(c.Id, (string.IsNullOrEmpty(c.Title) ? I18n.T("Hội thoại mới") : c.Title) + "  ·  " + c.Updated.ToString("dd/MM HH:mm")));
        if (keep != null)
            foreach (ChatRow r in chatList.Items) if (r.Id == keep) { chatList.SelectedItem = r; break; }
    }

    void ReloadProviders()
    {
        providerSel.Items.Clear();
        foreach (var p in AiStore.Providers)
            providerSel.Items.Add(new ComboBoxItem { Content = $"{p.Display} · {p.Model}", Tag = p.Id, IsSelected = chat?.ProviderId == p.Id || (chat == null && p.IsDefault) });
        if (providerSel.SelectedItem == null && providerSel.Items.Count > 0) providerSel.SelectedIndex = 0;
        ProviderChanged(save: false);
    }

    AiProvider? CurrentProvider()
    {
        if (providerSel.SelectedItem is ComboBoxItem it && it.Tag is string id) return AiStore.Get(id);
        return AiStore.Default;
    }

    void ProviderChanged(bool save = true)
    {
        var p = CurrentProvider();
        if (p == null) { modelBox.Text = ""; status.Text = I18n.T("Chưa có nhà cung cấp AI — bấm nút cài đặt để thêm khoá API."); return; }
        modelBox.Text = p.Model;
        modelBox.IsEnabled = true;
        if (save && chat != null) { chat.ProviderId = p.Id; chat.Model = p.Model; AiStore.SaveChat(chat); }
        status.Text = p.HasKey ? $"{AiPrefs.KindText(p.Kind)} · {p.Model}" : I18n.T("Chưa có khoá API cho nhà cung cấp này");
    }

    void SaveModel()
    {
        var p = CurrentProvider();
        if (p == null) return;
        var m = modelBox.Text.Trim();
        if (m == "" || m == p.Model) return;
        p.Model = m;
        AiStore.Upsert(p, p.IsDefault);
        if (chat != null) { chat.Model = m; AiStore.SaveChat(chat); }
        ReloadProviders();
    }

    async Task LoadModelsAsync()
    {
        var p = CurrentProvider();
        if (p == null) return;
        status.Text = I18n.T("Đang tải danh sách model…");
        var key = AiStore.KeyOf(p);
        var (ok, models, msg) = await AiClient.ListModelsAsync(p, key, CancellationToken.None);
        status.Text = ok ? msg : AiSecret.Redact(msg, key);
        if (!ok || models.Count == 0) return;
        var cm = new ContextMenu();
        foreach (var m in models.Take(200))
        {
            var mi = new MenuItem { Header = m };
            mi.Click += (_, _) => { modelBox.Text = m; SaveModel(); };
            cm.Items.Add(mi);
        }
        cm.PlacementTarget = modelBox;
        cm.IsOpen = true;
    }

    void NewChat()
    {
        if (chat != null) AiStore.SaveChat(chat);
        chat = AiStore.NewChat(CurrentProvider(), "");
        ReloadChatList();
        RenderChat();
        ShowEmptyState();
    }

    void LoadChat(string id)
    {
        if (chat != null) AiStore.SaveChat(chat);
        var c = AiStore.LoadChat(id);
        if (c == null) return;
        chat = c;
        foreach (ComboBoxItem it in providerSel.Items)
            if ((string)it.Tag == c.ProviderId) { providerSel.SelectedItem = it; break; }
        modelBox.Text = c.Model;
        RenderChat();
    }

    void DeleteChat()
    {
        if (chat == null) return;
        if (!Ui.Confirm("Xoá hội thoại", I18n.T("Xoá hội thoại này và toàn bộ nội dung?"), "Xoá", true)) return;
        AiStore.DeleteChat(chat.Id);
        chat = null;
        NewChat();
    }

    // ---------------- hiển thị tin nhắn ----------------
    void RenderChat()
    {
        messages.Children.Clear();
        toolCards.Clear();
        toolTexts.Clear();
        if (chat == null) return;
        foreach (var m in chat.Messages) AddMessageView(m, animate: false);
        ScrollToEnd();
    }

    void AddMessageView(AiMessage m, bool animate = true)
    {
        UIElement el = m.Role switch
        {
            "user" => Bubble(m.Content, HorizontalAlignment.Right, "AccentSoft", "Text"),
            "assistant" => Bubble(m.Content, HorizontalAlignment.Left, "Bg3", "Text", m.Error),
            "tool" => ToolCard(m.ToolName ?? "tool", m.ToolResult ?? m.Content, m.ToolOk != false, m.ToolSummary ?? ""),
            _ => new TextBlock { Text = m.Content, TextWrapping = TextWrapping.Wrap },
        };
        messages.Children.Add(el);
        if (animate) ScrollToEnd();
    }

    Border Bubble(string text, HorizontalAlignment align, string bgKey, string fgKey, bool error = false)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 3, 0, 3),
            HorizontalAlignment = align,
            MaxWidth = Math.Max(420, ActualWidth - 320),
        };
        card.SetResourceReference(Border.BackgroundProperty, bgKey);
        if (error) card.SetResourceReference(Border.BorderBrushProperty, "Bad");
        if (error) card.BorderThickness = new Thickness(1);
        var body = Markdown(text);
        body.SetResourceReference(TextBlock.ForegroundProperty, fgKey);
        card.Child = body;
        return card;
    }

    static StackPanel Markdown(string text)
    {
        var sp = new StackPanel();
        var lines = (text ?? "").Replace("\r", "").Split('\n');
        bool code = false;
        var buffer = new List<string>();
        void Flush()
        {
            if (buffer.Count == 0) return;
            var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.8, LineHeight = 19 };
            foreach (var inline in InlineParts(string.Join("\n", buffer))) tb.Inlines.Add(inline);
            sp.Children.Add(tb);
            buffer.Clear();
        }
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("```"))
            {
                Flush();
                code = !code;
                continue;
            }
            if (code) { buffer.Add(line); continue; }
            if (line.TrimStart().StartsWith("- ") || line.TrimStart().StartsWith("• ") || line.TrimStart().StartsWith("* "))
            {
                Flush();
                var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.8, Margin = new Thickness(10, 1, 0, 1), LineHeight = 19 };
                tb.Inlines.Add(new Run("•  ") { Foreground = Theme.Brush("Accent") });
                foreach (var inline in InlineParts(line.TrimStart()[2..])) tb.Inlines.Add(inline);
                sp.Children.Add(tb);
                continue;
            }
            if (line.Trim() == "") { Flush(); continue; }
            buffer.Add(line);
        }
        Flush();
        return sp;
    }

    static IEnumerable<Inline> InlineParts(string text)
    {
        // **đậm** và `mã` — đủ cho câu trả lời của trợ lý, không cần bộ dựng markdown đầy đủ
        int i = 0;
        var plain = new System.Text.StringBuilder();
        while (i < text.Length)
        {
            if (i + 1 < text.Length && text[i] == '*' && text[i + 1] == '*')
            {
                var end = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (end > 0)
                {
                    if (plain.Length > 0) { yield return new Run(plain.ToString()); plain.Clear(); }
                    yield return new Bold(new Run(text[(i + 2)..end]));
                    i = end + 2;
                    continue;
                }
            }
            if (text[i] == '`')
            {
                var end = text.IndexOf('`', i + 1);
                if (end > 0)
                {
                    if (plain.Length > 0) { yield return new Run(plain.ToString()); plain.Clear(); }
                    yield return new Run(text[(i + 1)..end]) { FontFamily = new FontFamily("Consolas"), Background = Theme.Brush("Bg") };
                    i = end + 1;
                    continue;
                }
            }
            plain.Append(text[i]);
            i++;
        }
        if (plain.Length > 0) yield return new Run(plain.ToString());
    }

    Border ToolCard(string name, string detail, bool ok, string summary)
    {
        var sp = new StackPanel();
        var head = new DockPanel();
        var icon = Ui.Icon(ok ? "check" : "x", 12, ok ? "Ok" : "Bad");
        icon.Margin = new Thickness(0, 0, 6, 0);
        head.Children.Add(icon);
        var label = new TextBlock
        {
            Text = $"{I18n.T("Công cụ")}: {name}{(string.IsNullOrEmpty(summary) ? "" : " — " + summary)}",
            FontSize = 11.8,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        head.Children.Add(label);
        sp.Children.Add(head);
        if (AiStore.Settings.ShowToolDetails && !string.IsNullOrWhiteSpace(detail))
        {
            var det = new TextBox
            {
                Text = detail.Length > 4000 ? detail[..4000] + "…" : detail,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11.3,
                MaxHeight = 190,
                Margin = new Thickness(0, 6, 0, 0),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            I18n.ForceLtr(det);
            sp.Children.Add(det);
        }
        var b = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 2, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxWidth = Math.Max(420, ActualWidth - 340),
            Child = sp,
        };
        b.SetResourceReference(Border.BackgroundProperty, "Bg");
        b.SetResourceReference(Border.BorderBrushProperty, ok ? "Line" : "Bad");
        b.BorderThickness = new Thickness(1);
        return b;
    }

    void ShowEmptyState()
    {
        if (chat is { Messages.Count: > 0 }) return;
        var card = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(18), Margin = new Thickness(0, 12, 0, 12), MaxWidth = 620 };
        card.SetResourceReference(Border.BackgroundProperty, "Bg3");
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = I18n.T("Tôi có thể giúp gì?"), FontSize = 15, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });
        sp.Children.Add(new TextBlock
        {
            Text = I18n.T("Tôi đọc được trạng thái các máy đang kết nối, chẩn đoán lỗi ADB, sắp xếp nhóm, chạy thao tác hàng loạt, chỉnh chất lượng hình và thực hiện tác vụ nhiều bước — sau khi bạn đồng ý các thao tác nhạy cảm."),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });
        foreach (var s in new[]
        {
            "Máy nào đang kết nối? Tình trạng pin ra sao?",
            "Vì sao máy #2 mất kết nối? Hãy xem nhật ký và chẩn đoán.",
            "Tạm dừng xem các máy đã chọn để giảm tải.",
            "Tạo nhóm \"Phòng thử nghiệm\" từ các máy đã chọn.",
            "Tóm tắt tình trạng tất cả các máy.",
        })
        {
            var b = Ui.Btn(s, "zap", "Btn", (_, _) => { input.Text = s; input.Focus(); input.CaretIndex = s.Length; });
            b.HorizontalAlignment = HorizontalAlignment.Left;
            b.Margin = new Thickness(0, 0, 0, 6);
            sp.Children.Add(b);
        }
        card.Child = sp;
        messages.Children.Add(card);
    }

    void ScrollToEnd()
    {
        Dispatcher.BeginInvoke(new Action(() => scroller.ScrollToEnd()), System.Windows.Threading.DispatcherPriority.Background);
    }

    // ---------------- gửi / nhận ----------------
    async Task SendAsync()
    {
        if (busy) return;
        var text = input.Text.Trim();
        if (text == "") return;
        var p = CurrentProvider();
        if (p == null) { Ui.Toast(I18n.T("Hãy thêm nhà cung cấp AI trước (khoá API)"), "err"); AiProvidersDialog.Show(this); return; }
        var key = AiStore.KeyOf(p);
        if (!p.HasKey && p.Kind != AiProviderKind.OpenAiCompatible)
        {
            Ui.Toast(I18n.T("Chưa có khoá API cho nhà cung cấp này"), "err");
            AiProvidersDialog.Show(this);
            return;
        }
        chat ??= AiStore.NewChat(p, "");
        if (chat.ProviderId != p.Id) { chat.ProviderId = p.Id; chat.Model = p.Model; }
        input.Text = "";
        busy = true;
        sendBtn.IsEnabled = false;
        stopBtn.IsEnabled = true;
        status.Text = I18n.T("Đang suy nghĩ…");
        // câu của người dùng do agent ghi vào lịch sử; ở đây chỉ hiện bong bóng trong khung chat
        messages.Children.Add(Bubble(text, HorizontalAlignment.Right, "AccentSoft", "Text"));
        ScrollToEnd();
        AiStore.SaveChat(chat);
        ReloadChatList();

        cts = new CancellationTokenSource();
        try
        {
            await Task.Run(() => agent.RunAsync(p, key, chat, text, cts.Token), cts.Token);
        }
        catch (OperationCanceledException) { OnAgentEvent(new AiEvent(AiEventKind.Stopped, "")); }
        catch (Exception e) { OnAgentEvent(new AiEvent(AiEventKind.Error, e.Message)); }
        finally
        {
            busy = false;
            sendBtn.IsEnabled = true;
            stopBtn.IsEnabled = false;
            status.Text = "";
            cts?.Dispose();
            cts = null;
            if (chat != null) AiStore.SaveChat(chat);
            ReloadChatList();
        }
    }

    void OnAgentEvent(AiEvent e)
    {
        switch (e.Kind)
        {
            case AiEventKind.Status:
                status.Text = e.Text == "thinking" ? I18n.T("Đang suy nghĩ…") : I18n.T("Đang thực hiện…");
                break;
            case AiEventKind.AssistantText:
                if (chat != null && chat.Messages.Count > 0 && chat.Messages[^1].Role == "assistant" && chat.Messages[^1].Content == e.Text) AddMessageView(chat.Messages[^1]);
                else messages.Children.Add(Bubble(e.Text, HorizontalAlignment.Left, "Bg3", "Text"));
                ScrollToEnd();
                break;
            case AiEventKind.Error:
                messages.Children.Add(Bubble(e.Text, HorizontalAlignment.Left, "Bg3", "Text", error: true));
                ScrollToEnd();
                break;
            case AiEventKind.ToolStart:
            {
                var card = ToolCard(e.Text, I18n.T("Đang thực hiện…"), true, "");
                messages.Children.Add(card);
                if (e.Call != null) toolCards[e.Call.Id] = card;
                ScrollToEnd();
                break;
            }
            case AiEventKind.ToolEnd:
            {
                if (e.Call != null && toolCards.Remove(e.Call.Id, out var card))
                {
                    messages.Children.Remove(card);
                    var replacement = ToolCard(e.Text, e.Result?.Content ?? "", e.Ok != false, e.Result?.Summary ?? "");
                    messages.Children.Add(replacement);
                }
                else messages.Children.Add(ToolCard(e.Text, e.Result?.Content ?? "", e.Ok != false, e.Result?.Summary ?? ""));
                ScrollToEnd();
                break;
            }
            case AiEventKind.Stopped:
                status.Text = I18n.T("Đã dừng");
                break;
            case AiEventKind.Usage:
            {
                var line = new TextBlock
                {
                    Text = e.Text,
                    FontSize = 11,
                    Margin = new Thickness(4, 0, 4, 8),
                    HorizontalAlignment = HorizontalAlignment.Left,
                };
                line.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
                messages.Children.Add(line);
                ScrollToEnd();
                break;
            }
        }
    }

    Task<bool> ApproveAsync(AiToolSpec spec, string summary)
    {
        var tcs = new TaskCompletionSource<bool>();
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var detail = I18n.T("Trợ lý muốn thực hiện thao tác nhạy cảm:") + "\n\n" + summary +
                         "\n\n" + I18n.T("Hãy kiểm tra các máy đích trước khi đồng ý.");
            tcs.SetResult(Ui.Confirm(I18n.T("Xác nhận thao tác của trợ lý"), detail, I18n.T("Thực hiện"), danger: spec.Sensitive));
        }));
        return tcs.Task;
    }
}

/// <summary>Chuỗi hiển thị theo từng loại nhà cung cấp.</summary>
static class AiPrefs
{
    public static string KindText(AiProviderKind k) => AiPresets.KindLabel(k);
}
