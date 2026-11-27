using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using ControlPhone.Ai;

namespace ControlPhone.Core;

/// <summary>
/// Tự kiểm tra không cần điện thoại (chạy: ControlPhone.exe --selfcheck): bản dịch + RTL, mã hoá DPAPI,
/// kho lưu nhà cung cấp AI, dựng yêu cầu/đọc trả lời của 3 giao thức, sổ công cụ, thông tin hỗ trợ.
/// Dùng trong CI trên Windows để chứng minh bản dựng chạy được, không chỉ biên dịch.
/// </summary>
public static class SelfCheck
{
    static int pass, fail, skip;
    static readonly List<string> lines = [];

    static void Check(bool ok, string name, string detail = "")
    {
        if (ok) pass++; else fail++;
        lines.Add($"{(ok ? "PASS" : "FAIL")}  {name}{(detail != "" ? "  | " + detail : "")}");
    }

    static void Skip(string name, string why)
    {
        skip++;
        lines.Add($"SKIP  {name}  | {why}");
    }

    public static int Run()
    {
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { } // tiếng Việt/Ả Rập hiện đúng trong log CI
        // thư mục dữ liệu tạm: không bao giờ chạm dữ liệu thật của người dùng
        var tmp = Path.Combine(Path.GetTempPath(), "cp-selfcheck-" + Guid.NewGuid().ToString("n")[..8]);
        Directory.CreateDirectory(tmp);
        Environment.SetEnvironmentVariable("CP_DATA_DIR", tmp);

        try
        {
            LocalizationChecks();
            SecretChecks();
            StoreChecks();
            ClientChecks();
            ToolChecks();
            RobustnessChecks();
            SanitizerChecks();
            UpdateChecks();
            BatchChecks();
            DiagChecks();
            SupportChecks();
        }
        catch (Exception e)
        {
            fail++;
            lines.Add("FAIL  selfcheck crashed  | " + e);
        }

        lines.Add($"\n{pass} passed, {fail} failed, {skip} skipped");
        var text = string.Join("\n", lines);
        Console.WriteLine(text);
        try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "controlphone-selfcheck.log"), text); } catch { }
        try { Directory.Delete(tmp, true); } catch { }
        return fail == 0 ? 0 : 1;
    }

    // ---------------- bản dịch + RTL ----------------
    static void LocalizationChecks()
    {
        foreach (var (code, _) in I18n.Langs)
        {
            I18n.Init(code);
            if (code == "vi")
            {
                Check(I18n.Lang == "vi" && !I18n.IsRtl, "i18n vi loads", $"{I18n.DictCount} keys");
                continue;
            }
            Check(I18n.Lang == code && I18n.DictCount > 500, $"i18n {code} loads", $"{I18n.DictCount} keys");
        }

        I18n.Init("ar");
        Check(I18n.IsRtl, "Arabic is RTL");
        Check(I18n.FlowDirection == System.Windows.FlowDirection.RightToLeft, "Arabic flow direction");
        Check(I18n.T("Đóng") == "إغلاق", "Arabic sample string", I18n.T("Đóng"));
        Check(I18n.T("Đã chọn {0} máy", 3).Contains('3'), "Arabic placeholder substitution", I18n.T("Đã chọn {0} máy", 3));
        Check(I18n.T("Nhà cung cấp AI") != "Nhà cung cấp AI", "New AI strings translated", I18n.T("Nhà cung cấp AI"));

        // số khoá của ar phải bằng en (bản dịch đầy đủ)
        int en = CountKeys("en"), ar = CountKeys("ar");
        Check(en > 600 && ar >= en, "ar covers every en key", $"en={en} ar={ar}");

        I18n.Init("en");
        Check(I18n.T("Đóng") == "Close", "English sample string", I18n.T("Đóng"));
        Check(!I18n.IsRtl, "English is LTR");
    }

    static int CountKeys(string code)
    {
        try
        {
            var f = Path.Combine(AppContext.BaseDirectory, "lang", code + ".js");
            var txt = File.ReadAllText(f);
            var json = txt[txt.IndexOf('{')..(txt.LastIndexOf('}') + 1)];
            json = System.Text.RegularExpressions.Regex.Replace(json, @",\s*}$", "}"); // tệp dịch có dấu phẩy cuối
            return JsonNode.Parse(json)!.AsObject().Count;
        }
        catch { return 0; }
    }

    // ---------------- DPAPI ----------------
    static void SecretChecks()
    {
        if (!AiSecret.Available)
        {
            Skip("DPAPI protect/unprotect", "not running on Windows");
            return;
        }
        const string key = "sk-test-1234567890-abcdef";
        var blob = AiSecret.Protect(key);
        Check(blob.Length > 20 && !blob.Contains("sk-test"), "DPAPI protects key", $"{blob.Length} chars");
        Check(AiSecret.Unprotect(blob) == key, "DPAPI roundtrip");
        var other = AiSecret.Protect(key);
        Check(other != blob, "DPAPI output differs each time (random IV)");
        Check(AiSecret.Unprotect("bm90LWEtYmxvYg==") == "", "DPAPI rejects foreign blob");
        Check(!AiSecret.Redact("GET https://x/y?key=" + key, key).Contains(key), "key redaction in messages", AiSecret.Redact("GET https://x/y?key=" + key, key));
        Check(!AiSecret.Redact("Authorization: Bearer " + key, key).Contains(key), "bearer redaction");
    }

    // ---------------- kho nhà cung cấp / hội thoại ----------------
    static void StoreChecks()
    {
        var p = new AiProvider
        {
            Name = "SelfCheck",
            Kind = AiProviderKind.OpenAi,
            BaseUrl = "https://api.openai.com/v1",
            Model = "gpt-4o-mini",
            MaxTokens = 300,
        };
        if (AiSecret.Available) AiStore.SetKey(p, "sk-selfcheck-secret-value");
        AiStore.Upsert(p, makeDefault: true);

        Check(AiStore.Providers.Count == 1 && AiStore.Default?.Id == p.Id, "provider saved and default");
        Check(!p.HasKey || p.KeyProtected != "sk-selfcheck-secret-value", "key is not stored in clear text");
        if (AiSecret.Available) Check(AiStore.KeyOf(p) == "sk-selfcheck-secret-value", "stored key decrypts for this Windows user");

        var aiJson = Path.Combine(Store.DataDir, "ai", "ai.json");
        var text = File.Exists(aiJson) ? File.ReadAllText(aiJson) : "";
        Check(text.Contains("SelfCheck"), "ai.json written");
        Check(!text.Contains("sk-selfcheck-secret-value"), "ai.json contains no plain API key");

        var chat = AiStore.NewChat(p, "SelfCheck chat");
        chat.Messages.Add(new AiMessage { Role = "user", Content = "hello" });
        chat.Messages.Add(new AiMessage { Role = "assistant", Content = "hi" });
        AiStore.SaveChat(chat);
        var back = AiStore.LoadChat(chat.Id);
        Check(back is { Messages.Count: 2 } && back.Messages[1].Content == "hi", "chat saved and reloaded");
        Check(AiStore.State.Chats.Any(c => c.Id == chat.Id), "chat listed in metadata");

        AiStore.DeleteChat(chat.Id);
        Check(AiStore.State.Chats.All(c => c.Id != chat.Id), "chat deleted");
        Check(!File.Exists(Path.Combine(Store.DataDir, "ai", "chats", chat.Id + ".json")), "chat file removed");

        // xuất cài đặt không được chứa khoá
        var exported = JsonSerializer.Serialize(Store.Settings);
        Check(!exported.Contains("sk-selfcheck"), "settings export has no key material");

        AiStore.DeleteAll();
        Check(AiStore.Providers.Count == 0, "delete all AI data");
    }

    // ---------------- dựng yêu cầu + đọc trả lời ----------------
    static void ClientChecks()
    {
        var msgs = new List<AiMessage>
        {
            new() { Role = "system", Content = "system prompt" },
            new() { Role = "user", Content = "list phones" },
            new() { Role = "assistant", Content = "", ToolCalls = [new AiToolCall("call_1", "list_devices", "{}")] },
            new() { Role = "tool", ToolCallId = "call_1", ToolName = "list_devices", Content = "no phones" },
        };
        var tools = new List<AiToolSpec> { new("list_devices", "List phones", "{\"type\":\"object\",\"properties\":{}}") };

        var openAi = new AiProvider { Kind = AiProviderKind.OpenAi, BaseUrl = "https://api.openai.com/v1", Model = "gpt-4o-mini" };
        var req = AiClient.BuildChat(openAi, "sk-key", msgs, tools);
        var body = JsonNode.Parse(req.Body)!.AsObject();
        Check(req.Url == "https://api.openai.com/v1/chat/completions", "OpenAI URL", req.Url);
        Check(req.Headers.TryGetValue("Authorization", out var auth) && auth == "Bearer sk-key", "OpenAI auth header");
        Check(body["model"]?.ToString() == "gpt-4o-mini", "OpenAI model in body");
        Check((body["tools"] as JsonArray)?.Count == 1, "OpenAI tools sent");
        Check(body["messages"]!.AsArray().Count == 4, "OpenAI messages converted");
        Check(!req.Body.Contains("system prompt\n\n") && req.Body.Contains("system prompt"), "OpenAI keeps system message");

        var gem = new AiProvider { Kind = AiProviderKind.Gemini, BaseUrl = "https://generativelanguage.googleapis.com", Model = "gemini-2.5-flash" };
        var greq = AiClient.BuildChat(gem, "gm-key", msgs, tools);
        var gbody = JsonNode.Parse(greq.Body)!.AsObject();
        Check(greq.Url.EndsWith("/v1beta/models/gemini-2.5-flash:generateContent"), "Gemini URL", greq.Url);
        Check(greq.Headers.ContainsKey("x-goog-api-key") && !greq.Url.Contains("gm-key"), "Gemini key in header, not URL");
        Check(gbody["systemInstruction"] != null, "Gemini system instruction");
        Check((gbody["tools"] as JsonArray)?[0]?["functionDeclarations"]?.AsArray().Count == 1, "Gemini function declarations");
        Check(!greq.Body.Contains("\"role\":\"system\""), "Gemini has no system role in contents");

        var ant = new AiProvider { Kind = AiProviderKind.Anthropic, BaseUrl = "https://api.anthropic.com", Model = "claude-sonnet-4-5" };
        var areq = AiClient.BuildChat(ant, "an-key", msgs, tools);
        var abody = JsonNode.Parse(areq.Body)!.AsObject();
        Check(areq.Url == "https://api.anthropic.com/v1/messages", "Anthropic URL", areq.Url);
        Check(areq.Headers["x-api-key"] == "an-key" && areq.Headers["anthropic-version"] == AiClient.AnthropicVersion, "Anthropic headers");
        Check(abody["system"]?.ToString() == "system prompt", "Anthropic system field");
        Check((abody["tools"] as JsonArray)?[0]?["input_schema"] != null, "Anthropic input_schema");

        // đọc câu trả lời mẫu
        var r1 = AiClient.ParseChat(openAi, "{\"choices\":[{\"message\":{\"content\":\"hello\",\"tool_calls\":[{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"list_devices\",\"arguments\":\"{}\"}}]}}],\"usage\":{\"prompt_tokens\":5,\"completion_tokens\":7}}");
        Check(r1.Text == "hello" && r1.ToolCalls.Count == 1 && r1.Usage.Total == 12, "parse OpenAI reply");

        var r2 = AiClient.ParseChat(gem, "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"hi\"},{\"functionCall\":{\"name\":\"list_devices\",\"args\":{}}}]}}],\"usageMetadata\":{\"promptTokenCount\":3,\"candidatesTokenCount\":4}}");
        Check(r2.Text == "hi" && r2.ToolCalls.Count == 1 && r2.Usage.Total == 7, "parse Gemini reply");

        var r3 = AiClient.ParseChat(ant, "{\"content\":[{\"type\":\"text\",\"text\":\"hi\"},{\"type\":\"tool_use\",\"id\":\"tu1\",\"name\":\"list_devices\",\"input\":{}}],\"usage\":{\"input_tokens\":2,\"output_tokens\":3}}");
        Check(r3.Text == "hi" && r3.ToolCalls.Count == 1 && r3.Usage.Total == 5, "parse Anthropic reply");
    }

    // ---------------- sổ công cụ ----------------
    static void ToolChecks()
    {
        var host = new FakeHost();
        var tools = new AiToolHost(host);
        var specs = tools.Specs;
        Check(specs.Count >= 20, "tool registry size", specs.Count + " tools");
        Check(specs.Select(s => s.Name).Distinct().Count() == specs.Count, "tool names unique");
        Check(specs.All(s => s.Description.Length > 20), "every tool has a description");
        bool schemasOk = true;
        foreach (var s in specs)
        {
            try
            {
                var o = JsonNode.Parse(s.ParametersJson)!.AsObject();
                if (o["type"]?.ToString() != "object" || o["properties"] == null) schemasOk = false;
            }
            catch { schemasOk = false; }
        }
        Check(schemasOk, "every tool has an object JSON schema");
        Check(tools.IsSensitive("shell") && tools.IsSensitive("install_apk") && tools.IsSensitive("send_text"), "sensitive tools flagged");
        Check(!tools.IsSensitive("list_devices"), "read-only tool not sensitive");

        // chạy công cụ đọc thật trên host giả
        var list = tools.ExecuteAsync("list_devices", "{}", CancellationToken.None).GetAwaiter().GetResult();
        Check(list.Ok && list.Content.Contains("#1"), "list_devices executes", list.Summary);
        var unknown = tools.ExecuteAsync("nope", "{}", CancellationToken.None).GetAwaiter().GetResult();
        Check(!unknown.Ok, "unknown tool rejected");
        var noTarget = tools.ExecuteAsync("send_text", "{\"text\":\"hi\"}", CancellationToken.None).GetAwaiter().GetResult();
        Check(!noTarget.Ok && noTarget.Content.Contains("No phones targeted"), "no unintended mass operation without a target");
        var guide = tools.ExecuteAsync("app_guide", "{\"topic\":\"wifi\"}", CancellationToken.None).GetAwaiter().GetResult();
        Check(guide.Ok && guide.Content.Contains("5555"), "app_guide returns real knowledge");
        var setBad = tools.ExecuteAsync("set_setting", "{\"key\":\"nope\",\"value\":\"1\"}", CancellationToken.None).GetAwaiter().GetResult();
        Check(!setBad.Ok, "unknown setting rejected");

        // lời nhắc hệ thống phải chứa trạng thái máy + quy tắc an toàn
        var prompt = AiKnowledge.SystemPrompt(host.Devices(), ["d1"], "3.0.0");
        Check(prompt.Contains("#1") && prompt.Contains("RULES") && prompt.Contains("approval"), "system prompt has state and rules");
    }

    sealed class FakeHost : IAiHost
    {
        public string AppVersion => "selfcheck";
        public IReadOnlyList<AiDeviceInfo> Devices() =>
        [
            new("d1", 1, "Phone One", "Pixel 7", "14", "online", "usb", 88, true, true, "192.168.1.20", ["Lab"], false, true, null),
            new("d2", 2, "Phone Two", "SM-G960F", "10", "offline", "wifi", null, null, false, "192.168.1.21", [], false, false, "offline"),
        ];
        public IReadOnlyList<string> SelectedIds() => [];
        public void SetSelection(IEnumerable<string> ids) { }
        public string DeviceDetails(string id) => "#1 Phone One\nModel: Pixel 7";
        public string Diagnostics() => "ADB connections: 2";
        public string ReadLogs(int lines) => "log line";
        public string SettingsSnapshot() => "Grid: 480px";
        public string ApplySetting(string key, string value) => key is "thumbFps" or "theme" ? "OK" : "ERR unknown setting: " + key;
        public Task<string> RunBatchAsync(string op, List<string> ids, Dictionary<string, object?> args, CancellationToken ct) => Task.FromResult("1 ok");
        public void OpenViewer(string id) { }
        public void OpenSupport() { }
        public void OpenAiSettings() { }
        public void OpenSettings() { }
        public void OpenChatWindow() { }
        public void Refresh() { }
        public void Toast(string message, string kind = "") { }
    }

    // ---------------- độ bền: bắt lỗi, thống kê sử dụng, rút gọn ngữ cảnh ----------------
    static void RobustnessChecks()
    {
        // 1) bắt lỗi: chi tiết phải có loại lỗi, thông điệp, stack và KHÔNG được chứa khoá
        Exception ex;
        try { throw new InvalidOperationException("boom sk-live-abcdef123456 with ?key=AIzaSyTOPSECRET12345"); }
        catch (Exception e) { ex = e; }
        var text = Crash.Format(ex, "selfcheck");
        Check(text.Contains(nameof(InvalidOperationException)) && text.Contains("boom"), "crash details have type and message");
        Check(text.Contains("StackTrace") || text.Contains("SelfCheck"), "crash details have a stack trace");
        Check(!text.Contains("sk-live-abcdef123456") && !text.Contains("AIzaSyTOPSECRET12345"), "crash details hide API keys", text.Split('\n')[1]);
        Check(text.Contains("selfcheck") && text.Contains("ControlPhone"), "crash details name the source and the app");

        // 2) ghi tệp + xoay vòng khi quá lớn
        var log = Crash.LogPath;
        try { File.Delete(log); } catch { }
        try { File.Delete(log + ".1"); } catch { }
        var written = Crash.Report(ex, "selfcheck");
        Check(File.Exists(log) && File.ReadAllText(log).Contains("boom"), "crash log written", log);
        Check(OpLog.Tail(5).Any(e => e.Error && e.Text.Contains("InvalidOperationException")), "crash added to the operation log");
        Check(written.Length > 40, "crash report returned to the caller");
        try { File.WriteAllText(log, new string('x', (int)Crash.MaxBytes + 32)); } catch { }
        Crash.Rotate();
        Check(File.Exists(log + ".1") && !File.Exists(log), "crash log rotates when full");
        try { File.Delete(log + ".1"); } catch { }

        // 3) rút gọn kết quả công cụ dài trước khi gửi lại cho mô hình
        var small = "ok: 2 phones";
        Check(Ai.AiAgent.TruncateForModel(small) == small, "short tool output is untouched");
        var big = new string('A', 6000) + new string('B', 5000) + new string('C', 1500);
        var cut = Ai.AiAgent.TruncateForModel(big);
        Check(cut.Length < big.Length && cut.Contains("characters omitted"), "long tool output truncated", $"{big.Length} -> {cut.Length}");
        Check(cut.StartsWith(new string('A', 100)) && cut.EndsWith(new string('C', 100)), "truncation keeps head and tail");

        // 4) thống kê sử dụng: cộng dồn, hiển thị, lưu lại sau khi đọc lại từ đĩa
        var u = new AiUsage(120, 30) + new AiUsage(80, 20);
        Check(u.InputTokens == 200 && u.OutputTokens == 50 && u.Total == 250 && u.Any, "usage adds up");
        var line = Ai.AiAgent.UsageText(u, TimeSpan.FromSeconds(3.4));
        Check(line.Contains("250") && line.Contains("200") && line.Contains("50") && line.Contains("3.4 s"), "usage line shows tokens and time", line);
        Check(!Ai.AiAgent.UsageText(new AiUsage(0, 0), TimeSpan.FromSeconds(12)).Contains("token"), "usage line without counters shows only time");

        var prov = AiStore.Providers.FirstOrDefault();
        if (prov == null)
        {
            prov = new AiProvider { Name = "selfcheck", Model = "m", Kind = AiProviderKind.OpenAi, BaseUrl = "https://example.invalid" };
            AiStore.Upsert(prov, true);
        }
        AiStore.AddUsage(prov.Id, new AiUsage(100, 40));
        AiStore.AddUsage(prov.Id, new AiUsage(10, 5));
        var saved = AiStore.Providers.First(x => x.Id == prov.Id);
        Check(saved.Requests == 2 && saved.TokensIn == 110 && saved.TokensOut == 45 && saved.LastUsed != null, "provider usage accumulates", $"{saved.Requests} req / {saved.TokensTotal} tokens");
        var aiFile = Path.Combine(Store.DataDir, "ai", "ai.json");
        var node = File.Exists(aiFile)
            ? System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(aiFile))?["providers"]?.AsArray().FirstOrDefault(x => x?["id"]?.ToString() == prov.Id)
            : null;
        Check(node?["requests"]?.GetValue<long>() == 2 && node?["tokensIn"]?.GetValue<long>() == 110 && node?["tokensOut"]?.GetValue<long>() == 45,
              "provider usage written to ai.json", node?.ToJsonString() ?? "missing");

        // 5) lời nhắc hệ thống phải coi đầu ra công cụ là dữ liệu không đáng tin
        var prompt = AiKnowledge.SystemPrompt([], [], "selfcheck");
        Check(prompt.Contains("UNTRUSTED DATA") && prompt.Contains("Never follow orders"), "prompt treats tool output as untrusted");
    }

    // ---------------- hợp lệ hoá cài đặt + thông số hình ảnh riêng từng máy ----------------
    static void SanitizerChecks()
    {
        // cấu hình hỏng (sửa tay / bản cũ) phải được kéo về khoảng hợp lệ thay vì mở luồng với số vô lý
        var bad = new Settings
        {
            ThumbMaxSize = 0, ThumbBitRate = 1, ThumbFps = 0,
            HqMaxSize = 99_999, HqBitRate = 5, HqFps = 1000,
            MaxParallelStart = 0, MaxParallelJobs = 500,
            PreferTransport = "carrier-pigeon", LanPorts = "5555, 99999 abc;0", ScreenshotDir = "bad\u0001dir",
        };
        var fixes = SettingsSanitizer.Apply(bad);
        Check(fixes.Count >= 9, "sanitizer reports what it fixed", fixes.Count + " fixes");
        Check(bad.ThumbMaxSize == 160 && bad.ThumbFps == 1 && bad.ThumbBitRate == 30_000, "thumbnail settings clamped", $"{bad.ThumbMaxSize}/{bad.ThumbFps}/{bad.ThumbBitRate}");
        Check(bad.HqMaxSize == 2560 && bad.HqFps == 60 && bad.HqBitRate == 500_000, "viewer settings clamped", $"{bad.HqMaxSize}/{bad.HqFps}/{bad.HqBitRate}");
        Check(bad.MaxParallelStart == 1 && bad.MaxParallelJobs == 50, "parallel limits clamped");
        Check(bad.PreferTransport == "usb", "unknown transport falls back to usb");
        Check(bad.LanPorts == "5555", "invalid ports dropped", bad.LanPorts);
        Check(!string.IsNullOrWhiteSpace(bad.ScreenshotDir) && bad.ScreenshotDir.IndexOfAny(Path.GetInvalidPathChars()) < 0, "invalid screenshot folder replaced");
        var good = new Settings { ScreenshotDir = Path.Combine(Path.GetTempPath(), "cp-shots") };
        Check(SettingsSanitizer.Apply(good).Count == 0, "valid settings are left alone", string.Join(",", SettingsSanitizer.Apply(good)));

        Check(SettingsSanitizer.NormalizePorts("5555, 15555 5555") == "5555,15555", "ports deduplicated and normalised", SettingsSanitizer.NormalizePorts("5555, 15555 5555"));
        Check(SettingsSanitizer.NormalizePorts("") == "5555" && SettingsSanitizer.NormalizePorts("70000") == "5555", "ports fall back to 5555");

        var ui = new UiPrefs { TileW = double.NaN, Sort = "hacked", Filter = "nonsense", Theme = "neon", Lang = "xx-YY", ShellHist = [.. Enumerable.Repeat("ls", 80)] };
        var uiFixes = SettingsSanitizer.Apply(ui);
        Check(ui.TileW == 180 && ui.Sort == "num" && ui.Filter == "all" && ui.Theme == "dark", "ui prefs sanitized", string.Join(",", uiFixes));
        Check(ui.Lang == "", "unknown language reset to system default");
        Check(ui.ShellHist.Count == 50, "shell history trimmed");
        Check(SettingsSanitizer.IsValidFilter("group:Phòng A") && SettingsSanitizer.IsValidFilter("bad") && !SettingsSanitizer.IsValidFilter("group:"), "filter validation");

        var ai = new Ai.AiSettings { MaxToolRounds = 999, ExtraSystemPrompt = new string('x', 9000) };
        SettingsSanitizer.Apply(ai);
        Check(ai.MaxToolRounds == 20 && ai.ExtraSystemPrompt.Length == 4000, "ai settings clamped");

        // thông số hình ảnh riêng từng máy: ghi đè thắng, để trống thì theo chung, giá trị vô lý bị kẹp
        var st = new Settings { ThumbMaxSize = 480, ThumbFps = 15, ThumbBitRate = 600_000, HqMaxSize = 1280, HqFps = 30, HqBitRate = 4_000_000 };
        var inherit = DeviceVideo.Thumb(null, st);
        Check(inherit == new VideoPlan(480, 600_000, 15), "no override uses global thumbnail settings", inherit.ToString());
        var weak = new DeviceMeta { ThumbMaxSize = 360, ThumbFps = 8 };
        var weakPlan = DeviceVideo.Thumb(weak, st);
        Check(weakPlan.MaxSize == 360 && weakPlan.MaxFps == 8 && weakPlan.BitRate == 600_000, "per-phone override applied", weakPlan.ToString());
        Check(DeviceVideo.Hq(weak, st) == new VideoPlan(1280, 4_000_000, 30), "phones without viewer overrides keep the global ones");
        var strong = new DeviceMeta { HqMaxSize = 1920, HqFps = 60, HqBitRate = 8_000_000 };
        Check(DeviceVideo.Hq(strong, st) == new VideoPlan(1920, 8_000_000, 60), "viewer override applied", DeviceVideo.Hq(strong, st).ToString());
        Check(DeviceVideo.Thumb(new DeviceMeta { ThumbFps = 0, ThumbMaxSize = -5 }, st) == inherit, "zero/negative overrides are ignored");
        Check(DeviceVideo.Thumb(new DeviceMeta { ThumbFps = 500, ThumbMaxSize = 9000 }, st) == new VideoPlan(1920, 600_000, 60), "out-of-range overrides clamped");
        Check(DeviceVideo.HasOverrides(weak) && !DeviceVideo.HasOverrides(new DeviceMeta()) && !DeviceVideo.HasOverrides(null), "override detection");
        Check(!DeviceVideo.HasOverrides(Store.MetaOrNull(null)) && Store.MetaOrNull("missing-device") == null, "meta lookup does not create entries");
    }

    // ---------------- gói chẩn đoán (ghi tệp thật rồi đọc lại) ----------------
    static void DiagChecks()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cp-diag-" + Guid.NewGuid().ToString("N")[..8]);
        var zip = Path.Combine(dir, "bundle.zip");
        try
        {
            OpLog.Add("selfcheck: khoá giả sk-SELFTESTSECRET1234567890 phải bị che");
            DiagBundle.Build(zip, ["#1\tMáy A\tusb\tonline\tAndroid 13"], 50);
            var entries = DiagBundle.Entries(zip);
            Check(File.Exists(zip) && entries.Length >= 4, "diagnostics bundle written", entries.Length + " entries");
            Check(entries.Contains("README.txt") && entries.Contains("versions.txt") && entries.Contains("oplog.txt") && entries.Contains("devices.txt"), "bundle has the expected files");
            Check(!entries.Any(e => e.Contains("ai.json")), "bundle never contains ai.json");
            Check(!ReadEntry(zip, "oplog.txt").Contains("SELFTESTSECRET"), "secrets are scrubbed from the bundle");
            var dev = ReadEntry(zip, "devices.txt");
            Check(dev.Contains("Máy A") && dev.Contains("Android 13"), "device list included");
            var ver = ReadEntry(zip, "versions.txt");
            Check(ver.Contains("ControlPhone") && ver.Contains(".NET"), "versions recorded");
            Check(DiagBundle.DefaultName().StartsWith("ControlPhone-diag-") && DiagBundle.DefaultName().EndsWith(".zip"), "bundle name is safe");
            DiagBundle.Build(zip, null, 5);
            Check(File.Exists(zip) && DiagBundle.Entries(zip).Length >= 4, "bundle can be rebuilt over an existing file");
            Check(ReadEntry(zip, "devices.txt").Contains("no phones connected"), "empty device list explained");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    static string ReadEntry(string zip, string name)
    {
        using var z = System.IO.Compression.ZipFile.OpenRead(zip);
        var e = z.GetEntry(name);
        if (e == null) return "";
        using var r = new StreamReader(e.Open());
        return r.ReadToEnd();
    }

    // ---------------- dừng thao tác hàng loạt ----------------
    static void BatchChecks()
    {
        var t = new BatchTask { Op = "test", Title = "kiểm tra", Total = 3 };
        Check(t.CanStop && !t.Token.IsCancellationRequested && t.Skipped == 0, "batch can be stopped while running");
        t.Stop();
        Check(!t.CanStop && t.Token.IsCancellationRequested, "stop cancels the queue");
        Check(t.StatusText == I18n.T("Đang dừng…"), "stopping task says so", t.StatusText);
        t.Stop();
        Check(!t.CanStop, "stop is idempotent");
        t.Finished = true;
        Check(!t.CanStop && t.StatusText == I18n.T("Xong · bấm để xem"), "finished task reports done", t.StatusText);

        var done = new BatchTask { Op = "test", Title = "xong", Total = 1, Finished = true };
        Check(!done.CanStop && done.Token.IsCancellationRequested == false, "finished task cannot be stopped");

        var skipped = new TaskResult("abc", 1, "máy", false, "", Skipped: true);
        Check(skipped.Skipped && !new TaskResult("abc", 1, "máy", true, "ok").Skipped, "skipped results are flagged");
    }

    // ---------------- kiểm tra bản cập nhật (phân tích thuần, không cần mạng) ----------------
    static void UpdateChecks()
    {
        Check(Updates.ParseTag("v3.2.0") == new Version(3, 2, 0), "release tag parsed", Updates.ParseTag("v3.2.0")?.ToString());
        Check(Updates.ParseTag("3.2") == new Version(3, 2), "two-part tag parsed");
        Check(Updates.ParseTag("v4.0.0-beta.1") == null && Updates.ParseTag("abc") == null && Updates.ParseTag("") == null && Updates.ParseTag(null) == null, "invalid tags rejected");
        Check(Updates.IsNewer(new Version(3, 3, 0), new Version(3, 2, 0)), "newer version detected");
        Check(!Updates.IsNewer(new Version(3, 2, 0), new Version(3, 2, 0)) && !Updates.IsNewer(new Version(3, 1, 9), new Version(3, 2, 0)), "same/older version not newer");
        Check(Updates.IsNewer(new Version(3, 10, 0), new Version(3, 9, 0)), "numeric compare, not string compare");
        Check(Updates.IsNewer(new Version(4, 0), new Version(3, 99, 99)), "major version wins");

        const string json = "{\"tag_name\":\"v99.0.0\",\"name\":\"ControlPhone 99\",\"html_url\":\"https://example.invalid/rel\","
            + "\"body\":\"notes here\",\"published_at\":\"2026-01-02T03:04:05Z\",\"draft\":false,\"prerelease\":false,"
            + "\"assets\":[{\"name\":\"ControlPhone-v99.0.0-win64.zip\",\"size\":84150874},"
            + "{\"name\":\"ControlPhone-Setup-v99.0.0-win64.exe\",\"size\":60422980}]}";
        var rel = Updates.Parse(json);
        Check(rel is { Tag: "v99.0.0" } && rel.Version == new Version(99, 0, 0), "release parsed", rel?.Tag);
        Check(rel?.ZipBytes == 84150874 && rel.SetupBytes == 60422980 && rel.HasInstaller, "release assets parsed", rel?.AssetsLine());
        Check(rel!.SetupUrl.EndsWith("/releases/download/v99.0.0/ControlPhone-Setup-v99.0.0-win64.exe"), "installer url built", rel.SetupUrl);
        Check(rel.ZipUrl.EndsWith(".zip") && rel.AssetsLine().Contains("Setup") && rel.AssetsLine().Contains("ZIP"), "asset line built");
        Check(rel.Url == "https://example.invalid/rel" && rel.Notes == "notes here" && rel.Published != null, "release metadata parsed");

        Check(Updates.Parse(json.Replace("\"draft\":false", "\"draft\":true")) == null, "draft release ignored");
        Check(Updates.Parse(json.Replace("\"prerelease\":false", "\"prerelease\":true")) == null, "prerelease ignored");
        Check(Updates.Parse("not json") == null && Updates.Parse("{}") == null && Updates.Parse("") == null && Updates.Parse(null) == null, "broken responses rejected");
        Check(Updates.Parse("{\"tag_name\":\"nightly\"}") == null, "release without a numeric tag rejected");

        Check(Updates.ShouldNotify(rel, null), "newer release notifies");
        Check(!Updates.ShouldNotify(rel, "v99.0.0"), "skipped release stays quiet");
        Check(Updates.ShouldNotify(rel, "v98.0.0"), "skipping an older release does not mute the new one");
        Check(!Updates.ShouldNotify(null, null), "no release, no notification");

        var ui = new UiPrefs { LastUpdateCheck = "yesterday-ish", SkipVersion = "nightly" };
        var fixes = SettingsSanitizer.Apply(ui);
        Check(ui.LastUpdateCheck == "" && ui.SkipVersion == "" && fixes.Count >= 2, "bogus update prefs cleared", string.Join(",", fixes));
        var ok = new UiPrefs { LastUpdateCheck = "2026-01-02 03:04", SkipVersion = "v3.2.0" };
        Check(SettingsSanitizer.Apply(ok).Count == 0, "valid update prefs kept");
    }

    // ---------------- thông tin hỗ trợ ----------------
    static void SupportChecks()
    {
        Check(SupportInfo.Email.Contains('@'), "support email present", SupportInfo.Email);
        Check(Uri.TryCreate(SupportInfo.TelegramChannel, UriKind.Absolute, out var c) && c.Scheme == "https", "telegram channel URL", SupportInfo.TelegramChannel);
        Check(Uri.TryCreate(SupportInfo.TelegramGroup, UriKind.Absolute, out var g) && g.Scheme == "https", "telegram group URL", SupportInfo.TelegramGroup);
        Check(Uri.TryCreate(SupportInfo.Facebook, UriKind.Absolute, out var f) && f.Scheme == "https", "facebook URL", SupportInfo.Facebook);
        Check(SupportInfo.ShamCash.Length >= 16, "sham cash id present");
        Check(SupportInfo.AllLinks().Contains(SupportInfo.ShamCash), "copy-all includes sham cash");
    }
}
