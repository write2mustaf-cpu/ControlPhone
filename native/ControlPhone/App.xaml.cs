using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using ControlPhone.Core;

namespace ControlPhone;

public partial class App : Application
{
    static Mutex? single;
    const string ShowEventName = "ControlPhone.Native.ShowWindow";

    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);

    /// <summary>
    /// Bản thứ hai không được thoát trong im lặng: nó nhờ bản đang chạy tự hiện lên (sự kiện có tên, hoặc cửa sổ chính),
    /// và nếu không ai trả lời thì nói rõ cho người dùng thay vì biến mất.
    /// </summary>
    static bool AskRunningInstanceToShow(int timeoutMs)
    {
        var deadline = Environment.TickCount64 + Math.Max(200, timeoutMs);
        do
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName))
                {
                    using (p)
                    {
                        if (p.Id == Environment.ProcessId) continue;
                        var h = p.MainWindowHandle;
                        if (h == IntPtr.Zero) continue;
                        ShowWindow(h, 9);            // SW_RESTORE
                        SetForegroundWindow(h);
                        return true;
                    }
                }
            }
            catch { }
            try
            {
                using var show = EventWaitHandle.OpenExisting(ShowEventName);
                show.Set();
                return true;
            }
            catch { }
            Thread.Sleep(150);
        }
        while (Environment.TickCount64 < deadline);
        return false;
    }

    /// <summary>Bản đầu tiên lắng nghe yêu cầu "hiện lên" của các lần mở sau.</summary>
    void ListenForShowRequests()
    {
        var t = new Thread(() =>
        {
            try
            {
                using var ev = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
                while (true) { ev.WaitOne(); Dispatcher.BeginInvoke(BringToFront); }
            }
            catch { }
        }) { IsBackground = true, Name = "show-window-listener" };
        t.Start();
    }

    void BringToFront()
    {
        if (MainWindow is not Window w) return;
        try
        {
            if (!w.IsVisible) w.Show();
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            w.Activate();
            w.Topmost = true; w.Topmost = false;
            var h = new System.Windows.Interop.WindowInteropHelper(w).Handle;
            if (h != IntPtr.Zero) { ShowWindow(h, 9); SetForegroundWindow(h); }
        }
        catch { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // tự kiểm tra không cần điện thoại (dùng trong CI): in PASS/FAIL rồi thoát với mã lỗi
        if (e.Args.Contains("--selfcheck"))
        {
            var code = Core.SelfCheck.Run();
            Shutdown(code);
            return;
        }
        // chỉ chạy 1 bản: mở lần 2 → đưa cửa sổ đang chạy lên trước
        StartupTrace.Mark("begin", $"v{typeof(App).Assembly.GetName().Version?.ToString(3)} · Windows {Environment.OSVersion.Version} · " +
            $"data {Store.DataDir} · args '{string.Join(' ', e.Args)}'");
        single = new Mutex(true, "ControlPhone.Native.SingleInstance", out bool first);
        if (!first && !e.Args.Contains("--restart"))
        {
            var shown = AskRunningInstanceToShow(2500);
            StartupTrace.Mark("second-instance", shown ? "first instance asked to show" : "no running window answered");
            if (!shown)
                MessageBox.Show(
                    "ControlPhone đã chạy nhưng không có cửa sổ nào phản hồi.\n\n" +
                    "Nếu bạn không thấy cửa sổ: mở Task Manager, kết thúc tiến trình ControlPhone, rồi chạy lại.\n\n" +
                    "ControlPhone is already running but no window answered. End the ControlPhone process in Task Manager, then start it again.",
                    "ControlPhone", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        if (!first) { try { single.WaitOne(10000); } catch (AbandonedMutexException) { } } // khởi động lại (đổi ngôn ngữ): chờ bản cũ thoát
        Crash.Install(this);
        // tránh các lần dọn rác dài làm khựng hình / chuột (luồng video cấp phát liên tục)
        System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;
        StartupTrace.Mark("pre-data");
        OfferPreviousData();
        Store.Load();
        // khởi động hỏng liên tiếp (chết trước khi cửa sổ sẵn sàng) → chế độ an toàn cho phiên này
        if (!SelfTest.On)
        {
            try
            {
                var fails = SessionGuard.Begin();
                if (SessionGuard.ShouldUseSafeMode(fails)) SafeMode.Activate(fails);
            }
            catch (Exception ex) { OpLog.Add("SessionGuard: " + ex.Message, error: true); }
        }
        StartupTrace.Mark("store", $"{Store.State.Devices.Count} máy{(Store.LoadError != null ? " · config lỗi: " + Store.LoadError : "")}");
        // chẩn đoán tại chỗ: chạy được cả khi giao diện không mở nổi
        if (e.Args.Contains("--doctor"))
        {
            var report = Core.Doctor.Report(Core.Doctor.Collect());
            var path = Core.Doctor.Write();
            StartupTrace.Mark("doctor", path);
            MessageBox.Show(report + "\n\nĐã lưu / saved: " + path, "ControlPhone — doctor",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(0);
            return;
        }
        I18n.Init(Store.Ui.Lang);
        Theme.Apply(Store.Ui.Theme);
        StartupTrace.Mark("theme", $"lang {I18n.Lang} · theme {Store.Ui.Theme}");
        ListenForShowRequests();
        base.OnStartup(e);
        // chọn chế độ: điều khiển tất cả máy / chỉ 1 điện thoại. "--mode all|pick|single <addr>" khi app tự khởi động lại.
        string? singleAddr = null;
        var args = e.Args.ToList();
        int mi = args.IndexOf("--mode");
        string mode = mi >= 0 && mi + 1 < args.Count ? args[mi + 1] : SelfTest.On ? "all" : "ask";
        if (mode == "single" && mi + 2 < args.Count) singleAddr = args[mi + 2];
        else if (mode is "ask" or "pick")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown; // đóng cửa sổ chọn chế độ không được thoát app
            var mw = new ModeWindow(startWithList: mode == "pick");
            mw.ShowDialog();
            if (!mw.Chosen) { Shutdown(); return; }
            singleAddr = mw.SingleAddr;
        }
        MainWindow main;
        try { main = new MainWindow(singleAddr); }
        catch (Exception ex)
        {
            var details = Crash.Report(ex, "startup");
            StartupTrace.Mark("window-failed", details.Split('\n').FirstOrDefault() ?? "");
            MessageBox.Show(
                I18n.T("ControlPhone không khởi động được. Chi tiết đã được ghi vào:") + "\n" + Crash.LogPath,
                "ControlPhone", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        MainWindow = main;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        main.Show();
        StartupTrace.Mark("shown", $"mode {(singleAddr == null ? "all" : "single")}");
    }

    /// <summary>Khởi động lại app ở chế độ khác (đóng cửa sổ hiện tại sau khi gọi).</summary>
    public static void RestartIn(string modeArgs) =>
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--restart --mode " + modeArgs) { UseShellExecute = false });

    /// <summary>
    /// Lần đầu chạy bản mới ở thư mục mới (chưa có data): tìm dữ liệu của bản cũ và hỏi có dùng không — để không mất
    /// số thứ tự / tên / nhóm khi cập nhật bằng cách giải nén ra thư mục khác.
    /// </summary>
    static void OfferPreviousData()
    {
        if (SelfTest.On || Environment.GetEnvironmentVariable("CP_DATA_DIR") != null || System.IO.File.Exists(Store.ConfigPath)) return;
        try
        {
            foreach (var c in DataImport.FindPrevious().Take(3))
            {
                var r = MessageBox.Show(
                    $"Tìm thấy dữ liệu của ControlPhone bản cũ:\n{c.Dir}\n{c.Phones} máy · sửa lần cuối {c.Modified:dd/MM/yyyy HH:mm}\n\n" +
                    "Dùng dữ liệu này cho bản mới (giữ số thứ tự, tên, nhóm, cài đặt)?\n\n" +
                    "Found data from an older ControlPhone (above). Use it for this version (keeps phone numbers, names, groups, settings)?",
                    "ControlPhone", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r == MessageBoxResult.Yes) { DataImport.Import(c.Dir); return; }
            }
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "ControlPhone", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        StartupTrace.Mark("exit", $"code {e.ApplicationExitCode}{(MainWindow == null ? " · không có cửa sổ nào được hiện" : "")}");
        if (!SelfTest.On) SessionGuard.Clean();
        Store.Flush();
        Store.FlushUi();
        try { single?.ReleaseMutex(); } catch { }
        base.OnExit(e);
    }
}
