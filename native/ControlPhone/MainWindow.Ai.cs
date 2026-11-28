using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using ControlPhone.Adb;
using ControlPhone.Ai;
using ControlPhone.Core;

namespace ControlPhone;

/// <summary>
/// Cầu nối giữa trợ lý AI và ứng dụng: mọi lệnh của trợ lý đi vào đúng các hàm mà giao diện đang dùng
/// (chọn máy, hàng đợi thao tác, phiên hình, cài đặt…), không có đường tắt riêng nào.
/// </summary>
public partial class MainWindow : IAiHost
{
    AiToolHost? aiTools;
    AiChatWindow? aiWindow;

    internal AiToolHost AiTools => aiTools ??= new AiToolHost(this);

    public string AppVersion => Version;

    T OnUi<T>(Func<T> f) => Dispatcher.CheckAccess() ? f() : Dispatcher.Invoke(f);

    void OnUiDo(Action a)
    {
        if (Dispatcher.CheckAccess()) a();
        else Dispatcher.Invoke(a);
    }

    Task OnUiAsync(Action a) => Dispatcher.InvokeAsync(a, DispatcherPriority.Normal).Task;

    public IReadOnlyList<AiDeviceInfo> Devices() => OnUi(() => manager.Devices
        .Where(d => !d.Pending)
        .Select(d => new AiDeviceInfo(
            d.Id, d.Num, d.DisplayName, d.Model, d.Android, d.Status, d.Transport ?? "—",
            d.Battery, d.Battery != null ? d.Charging : null, d.Root, d.Ip,
            d.Groups.ToList(), d.Paused, d.ControlSession != null || d.HasFrame,
            string.IsNullOrEmpty(d.Error) ? null : d.Error,
            d.Perf.Fps, d.Perf.Kbps, d.Perf.DecodeMs, d.Perf.Dropped,
            d.OpenedPlan is { } p ? $"{p.MaxSize}p/{p.MaxFps}fps/{p.BitRate / 1000}kbps" : "",
            d.Perf.Skipped, d.Perf.AgeMs))
        .ToList());

    public string SetQuality(string id, int maxSize, int fps, int kbps) => OnUi(() =>
    {
        var d = manager.Get(id);
        if (d == null) return "Phone not found.";
        var meta = Store.Meta(d.Id);
        if (maxSize > 0) meta.ThumbMaxSize = Math.Clamp(maxSize, 160, 1920);
        if (fps > 0) meta.ThumbFps = Math.Clamp(fps, 1, 60);
        if (kbps > 0) meta.ThumbBitRate = Math.Clamp(kbps, 30, 20_000) * 1000;
        Store.Save();
        manager.RestartSessions(d);
        var plan = DeviceVideo.Thumb(meta, Store.Settings);
        d.Changed();
        return $"#{d.Num} {d.DisplayName}: grid stream now {plan.MaxSize}p · {plan.MaxFps} fps · {plan.BitRate / 1000} kbps (stream reopened).";
    });

    public IReadOnlyList<string> SelectedIds() => OnUi(() => selected.ToList());

    public void SetSelection(IEnumerable<string> ids)
    {
        var list = ids.ToList();
        OnUiDo(() =>
        {
            selected.Clear();
            selected.UnionWith(list.Where(id => manager.Get(id) != null));
            RefreshSelection();
            RenderAll();
        });
    }

    public string DeviceDetails(string id)
    {
        var d = OnUi(() => manager.Get(id));
        if (d == null) return $"Unknown phone: {id}";
        var sb = new StringBuilder();
        sb.AppendLine($"#{d.Num} {d.DisplayName}  (id {d.Id})");
        sb.AppendLine($"Model: {d.Model} · {d.Manufacturer} · Android {d.Android} (SDK {d.Sdk})");
        sb.AppendLine($"Status: {d.Status}{(string.IsNullOrEmpty(d.Error) ? "" : " — " + d.Error)}");
        sb.AppendLine($"Transports: {(d.Transports.Count == 0 ? "—" : string.Join(", ", d.Transports.Select(t => t.Value.ToUpperInvariant() + " " + t.Key)))}");
        sb.AppendLine($"Battery: {(d.Battery?.ToString() ?? "?")}%{(d.Charging ? " (charging)" : "")}{(d.Temp is double t ? $" · {t:0.#} °C" : "")}");
        sb.AppendLine($"Root: {(d.Root ? "yes" : "no")} · Screen: {(d.ScreenOn switch { true => "on", false => "off", null => "unknown" })}");
        sb.AppendLine($"IP: {(string.IsNullOrEmpty(d.Ip) ? "—" : d.Ip)} · Video: {d.VideoW}x{d.VideoH}");
        sb.AppendLine($"Groups: {(d.Groups.Count == 0 ? "—" : string.Join(", ", d.Groups))}");
        sb.AppendLine($"View: {(d.Paused ? "paused" : "live")}{(d.Zoomed ? " · zoomed" : "")}{(d.HasFrame ? " · has image" : " · no image")}");
        if (d.InRecovery) sb.AppendLine("Recovery (TWRP) session active.");
        return sb.ToString().TrimEnd();
    }

    public string Diagnostics()
    {
        var r = OnUi(() => manager.Diagnostics());
        var sb = new StringBuilder();
        sb.AppendLine($"ADB connections: {r.AdbConnections} · ready devices: {r.DeviceConnections} · tiles: {r.Tiles} · duplicate serials: {r.DupSerials}");
        sb.AppendLine("ADB: " + AdbClient.AdbPath);
        foreach (var row in r.Rows)
            sb.AppendLine($"{row.Addr} · {row.State}{(row.Tid != "" ? " · t" + row.Tid : "")} · tile {row.Tile} · {(row.Ok ? "ok" : "problem")} · {row.Note}");
        return sb.ToString().TrimEnd();
    }

    public string ReadLogs(int lines)
    {
        var sb = new StringBuilder();
        sb.AppendLine(OpLog.Format(lines));
        try
        {
            var dir = Path.Combine(Store.DataDir, "logs");
            if (Directory.Exists(dir))
                foreach (var f in Directory.GetFiles(dir, "*.log").OrderByDescending(File.GetLastWriteTime).Take(3))
                {
                    sb.AppendLine($"--- {Path.GetFileName(f)} (tail) ---");
                    var all = File.ReadAllLines(f);
                    sb.AppendLine(string.Join("\n", all.Skip(Math.Max(0, all.Length - 40))));
                }
        }
        catch { }
        return sb.ToString().Trim();
    }

    public string SettingsSnapshot()
    {
        var s = Store.Settings;
        return string.Join("\n", new[]
        {
            $"Grid: max size {s.ThumbMaxSize}px · {s.ThumbBitRate / 1000} kbps · {s.ThumbFps} fps",
            $"Large view: max size {s.HqMaxSize}px · {s.HqBitRate / 1000} kbps · {s.HqFps} fps",
            $"Preferred transport: {s.PreferTransport} · auto-reconnect Wi-Fi: {s.AutoReconnectWifi}",
            $"Power on connect: {s.PowerOnConnect} · screen off on connect: {s.ScreenOffOnConnect} · software encoder: {s.SoftwareEncoder}",
            $"Focus viewer: {s.FocusViewer} · OTG/LAN auto: {s.LanAuto} (ports {s.LanPorts})",
            $"Parallel start: {s.MaxParallelStart} · parallel jobs: {s.MaxParallelJobs} · stop ADB on exit: {s.KillAdbOnExit}",
            $"Language: {I18n.Lang} · theme: {Theme.Current} · screenshot folder: {s.ScreenshotDir}",
            $"Data folder: {Store.DataDir} · AI providers: {AiStore.Providers.Count} · approval required: {AiStore.Settings.RequireApproval}",
        });
    }

    public string ApplySetting(string key, string value)
    {
        var s = Store.Settings;
        int I(string def, int min, int max) => int.TryParse(value, out var v) ? Math.Clamp(v, min, max) : int.Parse(def);
        bool B(bool def) => value.ToLowerInvariant() is "1" or "true" or "on" or "yes" or "بلى" or "نعم" or "có" ? true
            : value.ToLowerInvariant() is "0" or "false" or "off" or "no" or "لا" or "không" ? false : def;
        var restart = false;
        switch (key.ToLowerInvariant())
        {
            case "thumbmaxsize": s.ThumbMaxSize = I("480", 160, 1920); restart = true; break;
            case "thumbbitratekbps": s.ThumbBitRate = I("600", 30, 20000) * 1000; restart = true; break;
            case "thumbfps": s.ThumbFps = I("15", 1, 60); restart = true; break;
            case "hqmaxsize": s.HqMaxSize = I("1280", 320, 2560); break;
            case "hqbitratekbps": s.HqBitRate = I("4000", 500, 40000) * 1000; break;
            case "hqfps": s.HqFps = I("30", 5, 60); break;
            case "prefertransport":
                if (value is not ("usb" or "wifi")) return "ERR preferTransport must be usb or wifi";
                s.PreferTransport = value; break;
            case "autoreconnectwifi": s.AutoReconnectWifi = B(s.AutoReconnectWifi); break;
            case "screenoffonconnect": s.ScreenOffOnConnect = B(s.ScreenOffOnConnect); break;
            case "poweronconnect": s.PowerOnConnect = B(s.PowerOnConnect); restart = true; break;
            case "focusviewer": s.FocusViewer = B(s.FocusViewer); break;
            case "lanauto": s.LanAuto = B(s.LanAuto); break;
            case "lanports": s.LanPorts = string.Join(",", DeviceManager.ParsePorts(value)); break;
            case "maxparallelstart": s.MaxParallelStart = I("6", 1, 30); break;
            case "maxparalleljobs": s.MaxParallelJobs = I("10", 1, 50); break;
            case "killadbonexit": s.KillAdbOnExit = B(s.KillAdbOnExit); break;
            case "softwareencoder": s.SoftwareEncoder = B(s.SoftwareEncoder); restart = true; break;
            case "theme":
                if (value is not ("dark" or "light")) return "ERR theme must be dark or light";
                OnUiDo(() => { Theme.Apply(value); Store.Ui.Theme = Theme.Current; Store.SaveUi(); UpdateThemeIcon(); UpdateSyncUi(); Ui.DarkTitleBar(this); });
                return $"Theme set to {value}.";
            case "language":
                if (!I18n.Langs.Any(l => l.Code == value)) return "ERR unsupported language code";
                OnUiDo(() =>
                {
                    Store.Ui.Lang = value; Store.FlushUi();
                    App.RestartIn(ModeArgs);
                    Close();
                });
                return $"Language set to {value}; ControlPhone restarts to apply it.";
            default:
                return "ERR unknown setting: " + key;
        }
        Store.Save();
        OnUiDo(() =>
        {
            manager.ResetStartLimiter();
            if (restart) manager.RestartAll();
        });
        return $"OK: {key} = {value} (saved).";
    }

    public async Task<string> RunBatchAsync(string op, List<string> ids, Dictionary<string, object?> args, CancellationToken ct)
    {
        args ??= [];
        switch (op)
        {
            case "stream":
            {
                var action = args.TryGetValue("action", out var a) ? a?.ToString() ?? "restart" : "restart";
                await OnUiAsync(() =>
                {
                    var list = ids.Select(manager.Get).Where(d => d != null).Cast<PhoneDevice>().ToList();
                    if (action == "pause") SetPaused(ids, true);
                    else if (action == "resume") SetPaused(ids, false);
                    else foreach (var d in list) manager.RestartSessions(d);
                });
                return $"OK: {action} on {ids.Count} phone(s).";
            }
            case "connect":
            {
                var addr = args.TryGetValue("addr", out var av) ? av?.ToString() ?? "" : "";
                if (addr == "") return "FAIL: missing address";
                var r = await actions.ConnectAsync(addr);
                return (r.Ok ? "OK: " : "FAIL: ") + addr + " — " + r.Msg;
            }
            case "scan":
            {
                var port = args.TryGetValue("port", out var pv) && pv is int pi ? pi : 5555;
                var sb = new StringBuilder();
                foreach (var (name, ip, subnet) in Actions.LocalSubnets())
                {
                    sb.AppendLine($"Subnet {subnet}.0/24 ({name}, this PC {ip}):");
                    var (found, results) = await actions.ScanAsync(subnet, port);
                    sb.AppendLine($"  {found} host(s) with port {port} open");
                    foreach (var (addr, ok, msg) in results) sb.AppendLine($"  {(ok ? "OK" : "FAIL")} {addr} — {msg}");
                }
                return sb.Length == 0 ? "FAIL: no local network found" : sb.ToString().TrimEnd();
            }
            case "text":
            {
                var text = args.TryGetValue("text", out var tv) ? tv?.ToString() ?? "" : "";
                if (text == "") return "FAIL: missing text";
                args["text"] = text;
                break; // chạy qua máy thao tác hàng loạt để có kết quả từng máy, dừng và thử lại được
            }
            case "action":
            {
                var action = args.TryGetValue("action", out var av2) ? av2?.ToString() ?? "" : "";
                if (action == "") return "FAIL: missing action";
                await OnUiAsync(() =>
                {
                    var targets = ids.Select(manager.Get).Where(d => d is { IsOnline: true }).Cast<PhoneDevice>().ToList();
                    PhoneControl.Act(targets, action);
                });
                return $"OK: {action} sent to {ids.Count} phone(s).";
            }
        }

        var tcs = new TaskCompletionSource<BatchTask?>();
        await Dispatcher.InvokeAsync(() =>
        {
            try { tcs.SetResult(Batch(op, ids, args, op == "text" ? I18n.T("Gõ chữ") : op)); }
            catch (Exception e) { tcs.SetException(e); }
        });
        var task = await tcs.Task;
        if (task == null) return "FAIL: no online phone matched the request.";
        while (!task.Finished)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(150, ct);
        }
        if (task.Op == "text")
            return Typing.Summary(args.TryGetValue("text", out var tx) ? tx?.ToString() ?? "" : "",
                task.Results.Select(r => (r.Name, r.Ok, r.Msg)).ToList());
        var sb2 = new StringBuilder();
        sb2.AppendLine($"{task.OkCount} ok · {task.Fail} failed · {task.Total} phone(s) — {task.Op}");
        foreach (var r in task.Results)
            sb2.AppendLine($"{(r.Ok ? "OK" : "FAIL")} #{r.Vid} {r.Name}: {r.Msg}");
        return sb2.ToString().TrimEnd();
    }

    void IAiHost.OpenViewer(string id) => OnUiDo(() => OpenViewer(id));

    public void OpenSupport() => OnUiDo(DlgSupport);

    public void OpenAiSettings() => OnUiDo(() => AiProvidersDialog.Show(this));

    public void OpenSettings() => OnUiDo(DlgSettings);

    public void OpenChatWindow() => OnUiDo(ShowAiChat);

    public void Refresh() => OnUiDo(() => { RenderAll(); RefreshSelection(); });

    public void Toast(string message, string kind = "") => OnUiDo(() => Ui.Toast(message, kind));

    /// <summary>Mở cửa sổ trợ lý AI (tạo một lần, các lần sau chỉ đưa lên trước).</summary>
    internal void ShowAiChat()
    {
        if (aiWindow is { IsLoaded: true })
        {
            aiWindow.Activate();
            return;
        }
        aiWindow = new AiChatWindow(AiTools);
        aiWindow.Closed += (_, _) => aiWindow = null;
        aiWindow.Show();
    }
}
