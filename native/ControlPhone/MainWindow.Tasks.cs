using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ControlPhone.Core;

namespace ControlPhone;

public partial class MainWindow
{
    readonly Dictionary<int, Border> taskCards = [];

    // ---------------- thông báo ----------------
    void InitToasts()
    {
        Ui.ToastSink = (msg, kind, ms) => Dispatcher.BeginInvoke(() => ShowToast(msg, kind, ms));
    }

    void ShowToast(string msg, string kind, int ms)
    {
        var tb = new TextBlock { Text = msg, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White, FontSize = Theme.FsBody };
        var b = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(13, 9, 13, 9), Margin = new Thickness(0, 6, 0, 0), Child = tb, Opacity = 0 };
        b.Background = Theme.Brush(kind switch { "ok" => "ToastOk", "err" => "ToastErr", _ => "ToastInfo" });
        b.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.35 };
        b.MouseLeftButtonUp += (_, _) => ToastList.Items.Remove(b);
        ToastList.Items.Add(b);
        while (ToastList.Items.Count > 5) ToastList.Items.RemoveAt(0);
        b.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)));
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
            fade.Completed += (_, _) => ToastList.Items.Remove(b);
            b.BeginAnimation(OpacityProperty, fade);
        };
        t.Start();
    }

    // ---------------- tiến trình ----------------
    void OnTask(BatchTask task)
    {
        if (!taskCards.TryGetValue(task.Id, out var card))
        {
            card = BuildTaskCard(task);
            taskCards[task.Id] = card;
            TaskList.Items.Insert(0, card);
            while (TaskList.Items.Count > 6) { var last = (Border)TaskList.Items[^1]!; TaskList.Items.Remove(last); }
        }
        UpdateTaskCard(card, task);
        if (task.Finished && card.Tag is not "done")
        {
            card.Tag = "done";
            if (task.OpenResults) ShowTaskResults(task);
            if (task.Op == "screenshot" && task.OkCount > 0) Ui.Toast(T("Đã lưu {0} ảnh chụp", task.OkCount), "ok");
            if (task.Fail == 0)
            {
                var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
                t.Tick += (_, _) => { t.Stop(); TaskList.Items.Remove(card); taskCards.Remove(task.Id); };
                t.Start();
            }
        }
    }

    Border BuildTaskCard(BatchTask task)
    {
        var title = new TextBlock { FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Name = "tt" };
        var close = new Button { Content = Icons.Get("x"), FontSize = 10, Width = 22, Height = 20, MinHeight = 18, Padding = new Thickness(0) };
        close.SetResourceReference(StyleProperty, "IconGhost");
        var stop = new Button { Content = Icons.Get("stop"), Name = "stop", FontSize = 10, Width = 22, Height = 20, MinHeight = 18, Padding = new Thickness(0), Visibility = Visibility.Collapsed };
        stop.SetResourceReference(StyleProperty, "IconGhost");
        stop.ToolTip = T("Dừng thao tác này (các máy còn trong hàng đợi sẽ không chạy)");
        var head = new DockPanel();
        DockPanel.SetDock(close, Dock.Right); head.Children.Add(close);
        DockPanel.SetDock(stop, Dock.Right); head.Children.Add(stop);
        head.Children.Add(title);
        var bar = new ProgressBar { Margin = new Thickness(0, 6, 0, 6), Maximum = 100 };
        var info = new TextBlock { FontSize = 11.5 };
        var sp = new StackPanel(); sp.Children.Add(head); sp.Children.Add(bar); sp.Children.Add(info);
        var card = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(12, 9, 10, 9), Margin = new Thickness(0, 6, 0, 0), Child = sp, BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
        card.SetResourceReference(Border.BackgroundProperty, "Bg2");
        card.SetResourceReference(Border.BorderBrushProperty, "Line");
        card.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.3 };
        close.Click += (_, e) => { e.Handled = true; TaskList.Items.Remove(card); taskCards.Remove(task.Id); };
        stop.Click += (_, e) => { e.Handled = true; task.Stop(); UpdateTaskCard(card, task); };
        card.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is DependencyObject o && FindParent<Button>(o) == null) ShowTaskResults(task); };
        return card;
    }

    static T2? FindParent<T2>(DependencyObject o) where T2 : DependencyObject
    {
        for (var x = o; x != null; x = x is Visual ? VisualTreeHelper.GetParent(x) : LogicalTreeHelper.GetParent(x)) if (x is T2 t) return t;
        return null;
    }

    void UpdateTaskCard(Border card, BatchTask task)
    {
        var sp = (StackPanel)card.Child;
        var head = (DockPanel)sp.Children[0];
        ((TextBlock)head.Children[head.Children.Count - 1]).Text = task.Title;
        if (head.Children.OfType<Button>().FirstOrDefault(b => b.Name == "stop") is { } stop)
            stop.Visibility = task.CanStop ? Visibility.Visible : Visibility.Collapsed;
        ((ProgressBar)sp.Children[1]).Value = task.Progress;
        var info = (TextBlock)sp.Children[2];
        info.Inlines.Clear();
        info.Inlines.Add(new System.Windows.Documents.Run($"{task.Done}/{task.Total}   ") { Foreground = Theme.Brush("Muted") });
        info.Inlines.Add(new System.Windows.Documents.Run($"✓ {task.OkCount}   ") { Foreground = Theme.Brush("Ok") });
        if (task.Fail > 0) info.Inlines.Add(new System.Windows.Documents.Run($"✗ {task.Fail}   ") { Foreground = Theme.Brush("Bad") });
        if (task.Skipped > 0) info.Inlines.Add(new System.Windows.Documents.Run($"⏸ {task.Skipped}   ") { Foreground = Theme.Brush("Muted") });
        info.Inlines.Add(new System.Windows.Documents.Run(task.StatusText) { Foreground = Theme.Brush("Muted") });
        card.SetResourceReference(Border.BorderBrushProperty, task.Finished ? (task.Fail > 0 ? "Bad" : "Ok") : "Line");
    }

    void ShowTaskResults(BatchTask task)
    {
        var d = new Dlg(T("{0} — {1}/{2} thành công", task.Title, task.OkCount, task.Total), 800);
        if (task.DeviceIds.Count == 0 && task.Results.Count == 0) d.AddText("Không có máy nào.");

        var rows = new List<(string Id, TextBlock Icon, TextBox Msg, Button? Cancel)>();
        var ids = task.DeviceIds.Concat(task.Results.Select(r => r.Id)).Distinct().ToList();
        foreach (var id in ids)
        {
            var dev = manager.Get(id);
            var name = dev != null ? manager.LabelOf(dev) : id;
            var msg = new TextBox { Text = "", IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas"), FontSize = 11.5, MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var ic = Ui.Icon("clock", 13, "Muted");
            ic.Margin = new Thickness(0, 2, 8, 0);
            ic.VerticalAlignment = VerticalAlignment.Top;
            var nm = new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 10, 0) };
            Grid.SetColumn(nm, 1);
            Grid.SetColumn(msg, 2);
            var cancel = new Button { Content = Icons.Get("x"), FontSize = 10, Width = 22, Height = 20, MinHeight = 18, Padding = new Thickness(0), ToolTip = T("Huỷ riêng máy này, các máy khác vẫn chạy") };
            cancel.SetResourceReference(StyleProperty, "IconGhost");
            cancel.Visibility = Visibility.Collapsed;
            cancel.Click += (_, e) => { e.Handled = true; task.CancelDevice(id); UpdateTaskRows(); };
            Grid.SetColumn(cancel, 3);
            g.Children.Add(ic); g.Children.Add(nm); g.Children.Add(msg); g.Children.Add(cancel);
            d.Body.Children.Add(g);
            rows.Add((id, ic, msg, cancel));
        }

        void UpdateTaskRows()
        {
            foreach (var (id, icon, msg, cancel) in rows)
            {
                var r = task.Results.FirstOrDefault(x => x.Id == id);
                if (r == null)
                {
                    if (task.IsCancelled(id) || task.StopRequested) { msg.Text = I18n.T("Đã huỷ"); icon.Text = Icons.Get("x"); icon.Opacity = 0.55; }
                    else { msg.Text = I18n.T(task.Finished ? "Không chạy" : "Đang chờ…"); icon.Text = Icons.Get(task.Finished ? "pause" : "clock"); icon.Opacity = 0.55; }
                }
                else
                {
                    msg.Text = r.Skipped ? I18n.T("Đã dừng theo yêu cầu") : r.Msg != "" ? I18n.T(r.Msg) : I18n.T(r.Ok ? "OK" : "Lỗi");
                    icon.Text = Icons.Get(r.Skipped ? "pause" : r.Ok ? "check" : "x");
                    icon.Opacity = 1;
                }
                cancel.Visibility = !task.Finished && r == null && !task.IsCancelled(id) && !task.StopRequested
                    ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        void OnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => UpdateTaskRows();
        task.PropertyChanged += OnChanged;
        d.Closed += (_, _) => task.PropertyChanged -= OnChanged;
        UpdateTaskRows();

        if (!task.Finished)
            d.AddAction("Dừng tất cả", "Btn", () => { task.Stop(); UpdateTaskRows(); return false; }, "stop");
        if (task.Op == "screenshot")
            d.AddAction("Mở thư mục ảnh", "Btn", () =>
            {
                Directory.CreateDirectory(Store.Settings.ScreenshotDir);
                Process.Start(new ProcessStartInfo("explorer.exe", Store.Settings.ScreenshotDir) { UseShellExecute = true });
                return false;
            }, "folder");
        if (task.Fail > 0)
        {
            d.AddAction(T("Thử lại {0} máy lỗi", task.Fail), "Primary", () =>
            {
                var again = actions.Retry(task);
                if (again == null) { Ui.Toast(T("Không còn máy lỗi để thử lại"), "ok"); return false; }
                Ui.Toast(T("Đang thử lại trên {0} máy", again.Total), "ok");
                return true;
            }, "refresh");
            d.AddAction("Chọn các máy lỗi", "Btn", () =>
            {
                selected.Clear();
                foreach (var r in task.Results.Where(x => !x.Ok && !x.Skipped)) selected.Add(r.Id);
                RefreshSelection();
                return true;
            });
        }
        d.AddAction("Đóng", "Primary");
        d.Show();
    }
}
