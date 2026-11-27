using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using ControlPhone.Core;

namespace ControlPhone.Ai;

/// <summary>
/// Cầu nối giữa trợ lý AI và các chức năng thật của ControlPhone. Mọi thao tác giao diện đều được MainWindow
/// thực hiện trên luồng giao diện; lớp công cụ chỉ quyết định "làm gì trên máy nào".
/// </summary>
public interface IAiHost
{
    string AppVersion { get; }
    IReadOnlyList<AiDeviceInfo> Devices();
    IReadOnlyList<string> SelectedIds();
    void SetSelection(IEnumerable<string> ids);
    string DeviceDetails(string id);
    string Diagnostics();
    string ReadLogs(int lines);
    string SettingsSnapshot();
    /// <summary>Áp dụng một cài đặt trong danh sách cho phép; trả về mô tả kết quả.</summary>
    string ApplySetting(string key, string value);
    /// <summary>Chạy thao tác hàng loạt và chờ xong; trả về bảng kết quả từng máy.</summary>
    Task<string> RunBatchAsync(string op, List<string> ids, Dictionary<string, object?> args, CancellationToken ct);
    void OpenViewer(string id);
    void OpenSupport();
    void OpenAiSettings();
    void OpenSettings();
    void OpenChatWindow();
    void Refresh();
    void Toast(string message, string kind = "");
}

public sealed record AiToolResult(bool Ok, string Content, string Summary);

/// <summary>Sổ đăng ký công cụ: mô tả (JSON Schema) + cách chạy trên các máy đã chọn.</summary>
public sealed class AiToolHost(IAiHost host)
{
    public IAiHost Host { get; } = host;

    sealed record ToolDef(string Name, string Description, string Params, bool Sensitive,
        Func<JsonObject, CancellationToken, Task<AiToolResult>> Run);

    static string Schema(params (string Name, string Type, string Desc, bool Required)[] props)
    {
        var o = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() };
        var req = new JsonArray();
        foreach (var (name, type, desc, required) in props)
        {
            var p = new JsonObject { ["type"] = type, ["description"] = desc };
            if (type == "array") p["items"] = new JsonObject { ["type"] = "string" };
            o["properties"]![name] = p;
            if (required) req.Add(name);
        }
        if (req.Count > 0) o["required"] = req;
        return o.ToJsonString();
    }

    static readonly string DeviceArg = "Danh sách máy: số thứ tự (\"3\"), tên, hoặc id. Bỏ trống = các máy đang chọn trong ControlPhone.";
    static readonly string DeviceArgReq = "Máy đích: số thứ tự (\"3\"), tên hoặc id.";

    List<ToolDef>? defs;
    List<ToolDef> Defs => defs ??= BuildDefs();

    List<ToolDef> BuildDefs() =>
    [
        new("list_devices", "List every phone ControlPhone knows about, with number, name, model, Android version, status, transport (usb/wifi), battery, groups and whether it is selected.",
            Schema(), false, (a, ct) => Task.FromResult(ListDevices())),

        new("device_details", "Full details of one phone: model, Android/SDK, battery, temperature, IP, transports, groups, root, paused state, current errors.",
            Schema(("device", "string", DeviceArgReq, true)), false, (a, ct) => Task.FromResult(Details(a))),

        new("list_groups", "List device groups defined by the user.", Schema(), false, (a, ct) => Task.FromResult(Groups())),

        new("assign_group", "Add or remove phones from a named group (creates the group when missing).",
            Schema(("devices", "array", DeviceArg, false), ("group", "string", "Group name", true), ("mode", "string", "add (default) or remove", false)),
            false, (a, ct) => Task.FromResult(AssignGroup(a))),

        new("rename_device", "Give a phone a friendly name (kept across restarts).",
            Schema(("device", "string", DeviceArgReq, true), ("name", "string", "New name (max 40 chars)", true)),
            false, (a, ct) => Task.FromResult(Rename(a))),

        new("select_devices", "Change the ControlPhone selection. mode: replace (default), add, remove, all, none.",
            Schema(("devices", "array", DeviceArg, false), ("mode", "string", "replace|add|remove|all|none", false)),
            false, (a, ct) => Task.FromResult(Select(a))),

        new("stream_control", "Control the live view of phones. action: restart (reconnect the video stream), pause (stop streaming, still controllable), resume, zoom (open the large view of one phone), fit (fit the grid).",
            Schema(("devices", "array", DeviceArg, false), ("action", "string", "restart|pause|resume|zoom|fit", true)),
            false, (a, ct) => Stream(a, ct)),

        new("device_action", "Send a phone key/gesture: back, home, recents, power, volumeup, volumedown, unlock, screenoff, screenon, notif (pull notification bar), rotate.",
            Schema(("devices", "array", DeviceArg, false), ("action", "string", "back|home|recents|power|volumeup|volumedown|unlock|screenoff|screenon|notif|rotate", true)),
            true, (a, ct) => RunBatch("device_action", a, "action", "device action", ct)),

        new("send_text", "Type text into the currently focused field of the phones (any language, emoji). Use paste on the phone side.",
            Schema(("devices", "array", DeviceArg, false), ("text", "string", "Text to type", true)),
            true, (a, ct) => SendText(a, ct)),

        new("open_url", "Open a web link on the phones.",
            Schema(("devices", "array", DeviceArg, false), ("url", "string", "http(s) URL", true)),
            true, (a, ct) => RunBatch("open_url", a, "url", "open URL", ct)),

        new("app_control", "Manage apps on the phones. op: launch, stop, clear (clear app data), uninstall.",
            Schema(("devices", "array", DeviceArg, false), ("op", "string", "launch|stop|clear|uninstall", true), ("package", "string", "Package name, e.g. com.android.chrome", true)),
            true, (a, ct) => AppControl(a, ct)),

        new("install_apk", "Install an APK file that exists on the PC, on the phones.",
            Schema(("devices", "array", DeviceArg, false), ("path", "string", "Full path of the .apk on this PC", true)),
            true, (a, ct) => InstallApk(a, ct)),

        new("push_file", "Copy a file from the PC to the phones.",
            Schema(("devices", "array", DeviceArg, false), ("path", "string", "Full path on this PC", true), ("dir", "string", "Target folder on the phone (default /sdcard/Download/)", false)),
            true, (a, ct) => PushFile(a, ct)),

        new("shell", "Run an ADB shell command on the phones and return each phone's output. root=true runs it through su.",
            Schema(("devices", "array", DeviceArg, false), ("command", "string", "Shell command", true), ("root", "boolean", "Run as root (su -c)", false)),
            true, (a, ct) => Shell(a, ct)),

        new("connect_device", "Connect a phone over Wi-Fi/network by address (\"192.168.1.50\" or \"ip:port\").",
            Schema(("address", "string", "IP or IP:port", true)), false, (a, ct) => Connect(a, ct)),

        new("scan_lan", "Scan the PC's local networks for phones with ADB over network and connect them.",
            Schema(("port", "integer", "ADB TCP port to look for (default 5555)", false)), false, (a, ct) => Scan(a, ct)),

        new("screenshot", "Take a full-resolution screenshot of the phones and save it on the PC.",
            Schema(("devices", "array", DeviceArg, false)), false, (a, ct) => RunBatch("screenshot", a, null, "screenshot", ct)),

        new("get_settings", "Read the current ControlPhone settings (video quality, connection, performance).", Schema(), false,
            (a, ct) => Task.FromResult(new AiToolResult(true, host.SettingsSnapshot(), "read settings"))),

        new("set_setting", "Change one setting. Allowed keys: thumbMaxSize, thumbBitRateKbps, thumbFps, hqMaxSize, hqBitRateKbps, hqFps, preferTransport(usb|wifi), autoReconnectWifi, screenOffOnConnect, powerOnConnect, focusViewer, lanAuto, lanPorts, maxParallelStart, maxParallelJobs, killAdbOnExit, softwareEncoder, theme(dark|light), language(vi|en|ar|zh-CN|zh-TW|de|fr).",
            Schema(("key", "string", "Setting name", true), ("value", "string", "New value", true)),
            true, (a, ct) => Task.FromResult(SetSetting(a))),

        new("diagnostics", "Connection diagnostics: ADB state of every phone, duplicate serials, tile count.", Schema(), false,
            (a, ct) => Task.FromResult(new AiToolResult(true, host.Diagnostics(), "read diagnostics"))),

        new("read_logs", "Read the most recent ControlPhone operation log lines (batch operations, connection events, errors).",
            Schema(("lines", "integer", "How many lines (default 80, max 400)", false)), false, (a, ct) => Task.FromResult(ReadLogs(a))),

        new("app_guide", "Explain how ControlPhone works: topics devices, groups, sync, wifi, streaming, performance, ai, support, troubleshooting.",
            Schema(("topic", "string", "Topic name (optional)", false)), false, (a, ct) => Task.FromResult(Guide(a))),

        new("open_support", "Open the 'Our links & support' section (email, Telegram channel and group, Facebook page, Sham Cash ID).", Schema(), false,
            (a, ct) => { host.OpenSupport(); return Task.FromResult(new AiToolResult(true, "Opened the support section.", "opened support")); }),

        new("open_ai_settings", "Open the AI provider settings so the user can add or change an API key.", Schema(), false,
            (a, ct) => { host.OpenAiSettings(); return Task.FromResult(new AiToolResult(true, "Opened AI provider settings.", "opened AI settings")); }),
    ];

    public IReadOnlyList<AiToolSpec> Specs => Defs.Select(d => new AiToolSpec(d.Name, d.Description, d.Params, d.Sensitive)).ToList();

    public bool IsSensitive(string name) => Defs.FirstOrDefault(d => d.Name == name)?.Sensitive == true;

    public async Task<AiToolResult> ExecuteAsync(string name, string argumentsJson, CancellationToken ct)
    {
        var def = Defs.FirstOrDefault(d => d.Name == name);
        if (def == null) return new AiToolResult(false, $"Unknown tool: {name}", "unknown tool");
        JsonObject args;
        try { args = JsonNode.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson) as JsonObject ?? new JsonObject(); }
        catch { return new AiToolResult(false, "Arguments are not valid JSON.", "bad arguments"); }
        try { return await def.Run(args, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { return new AiToolResult(false, "Stopped by the user.", "stopped"); }
        catch (Exception e) { return new AiToolResult(false, e.Message, "error: " + e.Message); }
    }

    // ---------------- tiện ích tham số ----------------
    static string S(JsonObject a, string k, string def = "") =>
        a[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : a[k] is JsonValue n && n.GetValueKind() == System.Text.Json.JsonValueKind.Number ? a[k]!.ToString() : def;

    static bool B(JsonObject a, string k) => a[k] is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    static int I(JsonObject a, string k, int def)
    {
        if (a[k] is not JsonValue v) return def;
        if (v.TryGetValue<int>(out var i)) return i;
        return int.TryParse(v.ToString(), out var p) ? p : def;
    }

    static List<string> Arr(JsonObject a, string k) =>
        a[k] is JsonArray arr ? arr.Select(x => x?.ToString() ?? "").Where(s => s.Trim() != "").ToList() : [];

    List<PhoneTarget> Targets(JsonObject a)
    {
        var tokens = Arr(a, "devices");
        var single = S(a, "device");
        if (single != "") tokens.Insert(0, single);
        var all = B(a, "all");
        return Resolve(tokens, all);
    }

    sealed record PhoneTarget(string Id, int Num, string Name, string Status);

    List<PhoneTarget> AllTargets() => host.Devices()
        .Select(d => new PhoneTarget(d.Id, d.Num, d.Name, d.Status)).ToList();

    List<PhoneTarget> Resolve(List<string> tokens, bool all)
    {
        var devices = AllTargets();
        if (all) return devices;
        if (tokens.Count == 0)
        {
            var sel = host.SelectedIds();
            return devices.Where(d => sel.Contains(d.Id)).ToList();
        }
        var res = new List<PhoneTarget>();
        foreach (var raw in tokens)
        {
            var t = raw.Trim().TrimStart('#');
            var hit = devices.FirstOrDefault(d => d.Id == raw)
                      ?? devices.FirstOrDefault(d => int.TryParse(t, out var n) && d.Num == n)
                      ?? devices.FirstOrDefault(d => string.Equals(d.Name, t, StringComparison.OrdinalIgnoreCase))
                      ?? devices.FirstOrDefault(d => d.Name.Contains(t, StringComparison.OrdinalIgnoreCase));
            if (hit != null && !res.Contains(hit)) res.Add(hit);
        }
        return res;
    }

    static string NoTargets() =>
        "No phones targeted. Select phones in ControlPhone first, or pass their numbers/names in \"devices\", or set \"all\": true to target every phone.";

    static string Ids(IEnumerable<PhoneTarget> t) => string.Join(", ", t.Select(x => $"#{x.Num} {x.Name}"));

    // ---------------- công cụ đọc ----------------
    AiToolResult ListDevices()
    {
        var devs = host.Devices();
        if (devs.Count == 0) return new AiToolResult(true, "No phones are known yet. Connect a phone by USB or add it over Wi-Fi.", "0 phones");
        var sel = host.SelectedIds();
        var sb = new StringBuilder();
        sb.AppendLine($"{devs.Count} phone(s) known · {devs.Count(d => d.Status == "online")} online · selected: {(sel.Count == 0 ? "none" : string.Join(", ", devs.Where(d => sel.Contains(d.Id)).Select(d => "#" + d.Num)))}");
        foreach (var d in devs.OrderBy(d => d.Num))
        {
            var flags = new List<string>();
            if (sel.Contains(d.Id)) flags.Add("selected");
            if (d.Paused) flags.Add("paused");
            if (d.Root) flags.Add("root");
            if (d.Streaming) flags.Add("streaming");
            sb.AppendLine($"#{d.Num} · {d.Name} · {d.Model} · Android {d.Android} · {d.Status} · {d.Transport} · battery {(d.Battery?.ToString() ?? "?")}%{(d.Charging == true ? " charging" : "")} · groups: {(d.Groups.Count == 0 ? "-" : string.Join("/", d.Groups))}{(flags.Count > 0 ? " · " + string.Join(", ", flags) : "")}{(string.IsNullOrEmpty(d.Error) ? "" : " · error: " + d.Error)}");
        }
        return new AiToolResult(true, sb.ToString().TrimEnd(), $"{devs.Count} phones listed");
    }

    AiToolResult Details(JsonObject a)
    {
        var t = Targets(a);
        if (t.Count == 0) return new AiToolResult(false, NoTargets(), "no target");
        return new AiToolResult(true, string.Join("\n\n", t.Select(x => host.DeviceDetails(x.Id))), "details of " + Ids(t));
    }

    AiToolResult Groups()
    {
        var groups = Store.State.Groups;
        if (groups.Count == 0) return new AiToolResult(true, "No groups yet.", "0 groups");
        var sb = new StringBuilder();
        foreach (var g in groups)
        {
            var members = host.Devices().Where(d => d.Groups.Contains(g)).OrderBy(d => d.Num).Select(d => "#" + d.Num).ToList();
            sb.AppendLine($"{g}: {(members.Count == 0 ? "(empty)" : string.Join(", ", members))}");
        }
        return new AiToolResult(true, sb.ToString().TrimEnd(), $"{groups.Count} groups");
    }

    // ---------------- công cụ thay đổi ----------------
    AiToolResult AssignGroup(JsonObject a)
    {
        var group = S(a, "group").Trim();
        if (group == "") return new AiToolResult(false, "Missing group name.", "missing group");
        var t = Targets(a);
        if (t.Count == 0) return new AiToolResult(false, NoTargets(), "no target");
        var remove = S(a, "mode", "add").Equals("remove", StringComparison.OrdinalIgnoreCase);
        foreach (var x in t)
        {
            var meta = Store.Meta(x.Id);
            meta.Groups.RemoveAll(g => g.Equals(group, StringComparison.OrdinalIgnoreCase));
            if (!remove) meta.Groups.Add(group);
        }
        if (!remove && !Store.State.Groups.Contains(group, StringComparer.OrdinalIgnoreCase)) Store.State.Groups.Add(group);
        Store.Save();
        host.Refresh();
        return new AiToolResult(true, $"{(remove ? "Removed" : "Added")} {Ids(t)} {(remove ? "from" : "to")} group \"{group}\".", $"group {group} updated");
    }

    AiToolResult Rename(JsonObject a)
    {
        var t = Targets(a);
        if (t.Count != 1) return new AiToolResult(false, "Rename needs exactly one phone.", "need one phone");
        var name = S(a, "name").Trim();
        if (name == "") return new AiToolResult(false, "Missing new name.", "missing name");
        if (name.Length > 40) name = name[..40];
        Store.Meta(t[0].Id).Label = name;
        Store.Save();
        host.Refresh();
        return new AiToolResult(true, $"#{t[0].Num} is now called \"{name}\".", "renamed");
    }

    AiToolResult Select(JsonObject a)
    {
        var mode = S(a, "mode", "replace").ToLowerInvariant();
        var all = AllTargets();
        var sel = host.SelectedIds().ToList();
        switch (mode)
        {
            case "all": host.SetSelection(all.Select(x => x.Id)); return new AiToolResult(true, $"Selected all {all.Count} phones.", "selected all");
            case "none": host.SetSelection([]); return new AiToolResult(true, "Cleared the selection.", "selection cleared");
        }
        var t = Targets(a);
        if (t.Count == 0) return new AiToolResult(false, NoTargets(), "no target");
        var next = mode switch
        {
            "add" => sel.Concat(t.Select(x => x.Id)).Distinct().ToList(),
            "remove" => sel.Where(id => t.All(x => x.Id != id)).ToList(),
            _ => t.Select(x => x.Id).ToList(),
        };
        host.SetSelection(next);
        return new AiToolResult(true, $"Selection is now {next.Count} phone(s): {Ids(t)}.", "selection updated");
    }

    async Task<AiToolResult> Stream(JsonObject a, CancellationToken ct)
    {
        var action = S(a, "action").ToLowerInvariant();
        if (action == "fit") { host.Refresh(); return new AiToolResult(true, "Grid fitted to the window.", "fitted grid"); }
        var t = Targets(a);
        if (t.Count == 0) return new AiToolResult(false, NoTargets(), "no target");
        if (action == "zoom")
        {
            if (t.Count != 1) return new AiToolResult(false, "zoom needs exactly one phone.", "need one phone");
            host.OpenViewer(t[0].Id);
            return new AiToolResult(true, $"Opened the large view of #{t[0].Num} {t[0].Name}.", "opened viewer");
        }
        var op = action switch { "pause" => "pause", "resume" => "resume", _ => "restart" };
        return await RunBatch("stream", a, null, op, ct).ConfigureAwait(false);
    }

    // ---------------- công cụ chạy hàng loạt ----------------
    async Task<AiToolResult> RunBatch(string kind, JsonObject a, string? argName, string label, CancellationToken ct)
    {
        var t = Targets(a);
        if (t.Count == 0) return new AiToolResult(false, NoTargets(), "no target");
        string op;
        var p = new Dictionary<string, object?>();
        switch (kind)
        {
            case "device_action":
                op = "action";
                var act = S(a, "action").ToLowerInvariant();
                var allowed = new[] { "back", "home", "recents", "power", "volumeup", "volumedown", "unlock", "screenoff", "screenon", "notif", "rotate" };
                if (!allowed.Contains(act)) return new AiToolResult(false, "Unknown action. Allowed: " + string.Join(", ", allowed), "bad action");
                p["action"] = act;
                label = "action " + act;
                break;
            case "open_url":
                op = "url";
                var url = S(a, "url").Trim();
                if (url == "") return new AiToolResult(false, "Missing url.", "missing url");
                if (!url.Contains("://")) url = "https://" + url;
                p["url"] = url;
                break;
            default:
                op = kind;
                break;
        }
        var text = await host.RunBatchAsync(op, t.Select(x => x.Id).ToList(), p, ct).ConfigureAwait(false);
        var ok = !text.Contains("FAIL") && !text.Contains("failed");
        return new AiToolResult(ok, text, $"{label} on {Ids(t)}");
    }

    async Task<AiToolResult> SendText(JsonObject a, CancellationToken ct)
    {
        var text = S(a, "text");
        if (text == "") return new AiToolResult(false, "Missing text.", "missing text");
        var t = Targets(a);
        if (t.Count == 0) return new AiToolResult(false, NoTargets(), "no target");
        var p = new Dictionary<string, object?> { ["text"] = text };
        var res = await host.RunBatchAsync("text", t.Select(x => x.Id).ToList(), p, ct).ConfigureAwait(false);
        return new AiToolResult(true, res, $"typed text on {Ids(t)}");
    }

    async Task<AiToolResult> AppControl(JsonObject a, CancellationToken ct)
    {
        var opName = S(a, "op").ToLowerInvariant();
        var pkg = S(a, "package").Trim();
        if (pkg == "") return new AiToolResult(false, "Missing package name.", "missing package");
        var op = opName switch { "launch" => "launch", "stop" => "stopapp", "clear" => "clearapp", "uninstall" => "uninstall", _ => "" };
        if (op == "") return new AiToolResult(false, "op must be launch, stop, clear or uninstall.", "bad op");
        var t = Targets(a);
        if (t.Count == 0) return new AiToolResult(false, NoTargets(), "no target");
        var res = await host.RunBatchAsync(op, t.Select(x => x.Id).ToList(), new() { ["pkg"] = pkg }, ct).ConfigureAwait(false);
        return new AiToolResult(true, res, $"{opName} {pkg} on {Ids(t)}");
    }

    async Task<AiToolResult> InstallApk(JsonObject a, CancellationToken ct)
    {
        var path = S(a, "path").Trim().Trim('"');
        if (!File.Exists(path)) return new AiToolResult(false, "File not found on this PC: " + path, "file not found");
        var t = Targets(a);
        if (t.Count == 0) return new AiToolResult(false, NoTargets(), "no target");
        var res = await host.RunBatchAsync("install", t.Select(x => x.Id).ToList(), new() { ["file"] = path }, ct).ConfigureAwait(false);
        return new AiToolResult(true, res, $"installed {Path.GetFileName(path)} on {Ids(t)}");
    }

    async Task<AiToolResult> PushFile(JsonObject a, CancellationToken ct)
    {
        var path = S(a, "path").Trim().Trim('"');
        if (!File.Exists(path)) return new AiToolResult(false, "File not found on this PC: " + path, "file not found");
        var dir = S(a, "dir", "/sdcard/Download/").Trim();
        if (dir == "") dir = "/sdcard/Download/";
        var t = Targets(a);
        if (t.Count == 0) return new AiToolResult(false, NoTargets(), "no target");
        var res = await host.RunBatchAsync("push", t.Select(x => x.Id).ToList(),
            new() { ["file"] = path, ["dir"] = dir, ["name"] = Path.GetFileName(path) }, ct).ConfigureAwait(false);
        return new AiToolResult(true, res, $"copied {Path.GetFileName(path)} to {Ids(t)}");
    }

    async Task<AiToolResult> Shell(JsonObject a, CancellationToken ct)
    {
        var cmd = S(a, "command").Trim();
        if (cmd == "") return new AiToolResult(false, "Missing command.", "missing command");
        var t = Targets(a);
        if (t.Count == 0) return new AiToolResult(false, NoTargets(), "no target");
        var res = await host.RunBatchAsync("shell", t.Select(x => x.Id).ToList(),
            new() { ["cmd"] = cmd, ["root"] = B(a, "root") }, ct).ConfigureAwait(false);
        return new AiToolResult(true, res, $"shell on {Ids(t)}");
    }

    async Task<AiToolResult> Connect(JsonObject a, CancellationToken ct)
    {
        var addr = S(a, "address").Trim();
        if (addr == "") return new AiToolResult(false, "Missing address.", "missing address");
        var res = await host.RunBatchAsync("connect", [], new() { ["addr"] = addr }, ct).ConfigureAwait(false);
        return new AiToolResult(true, res, "connect " + addr);
    }

    async Task<AiToolResult> Scan(JsonObject a, CancellationToken ct)
    {
        var port = I(a, "port", 5555);
        var res = await host.RunBatchAsync("scan", [], new() { ["port"] = port }, ct).ConfigureAwait(false);
        return new AiToolResult(true, res, "scan LAN port " + port);
    }

    AiToolResult SetSetting(JsonObject a)
    {
        var key = S(a, "key").Trim();
        var value = S(a, "value").Trim();
        if (key == "" || value == "") return new AiToolResult(false, "key and value are required.", "missing args");
        var msg = host.ApplySetting(key, value);
        var ok = !msg.StartsWith("ERR", StringComparison.OrdinalIgnoreCase);
        return new AiToolResult(ok, msg, $"setting {key}={value}");
    }

    AiToolResult ReadLogs(JsonObject a)
    {
        var n = Math.Clamp(I(a, "lines", 80), 10, 400);
        return new AiToolResult(true, host.ReadLogs(n), $"last {n} log lines");
    }

    AiToolResult Guide(JsonObject a)
    {
        var topic = S(a, "topic").ToLowerInvariant();
        return new AiToolResult(true, AiKnowledge.Guide(topic), "app guide" + (topic == "" ? "" : ": " + topic));
    }
}
