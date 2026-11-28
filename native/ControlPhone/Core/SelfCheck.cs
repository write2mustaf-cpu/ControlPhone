using System.Windows.Input;
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
            SafeModeChecks();
            WatchdogChecks();
            DeviceLogChecks();
            StreamChecks();
            BackupChecks();
            ShortcutChecks();
            TypingChecks();
            ManualOrderChecks();
            KeyboardChecks();
            TargetChecks();
            ThemeChecks();
            HistoryChecks();
            PerfChecks();
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

        // Toàn bộ gói dịch phải thật sự đã dịch: không giá trị nào trùng khoá, mọi câu tiếng Ả Rập phải có chữ Ả Rập,
        // và không bản dịch nào được làm mất {0}/{1} (mất placeholder = mất dữ liệu người dùng khi hiện).
        var arPack = Pack("ar");
        Check(arPack.Count > 900, "Arabic pack is complete", arPack.Count + " keys");
        Check(arPack.All(kv => kv.Value.Trim() != kv.Key.Trim()), "no Arabic value is left in Vietnamese");
        Check(arPack.All(kv => System.Text.RegularExpressions.Regex.IsMatch(kv.Value, "[\u0600-\u06FF]")),
            "every Arabic value contains Arabic script", arPack.Count.ToString());
        var lost = arPack.Where(kv => Placeholders(kv.Key) != Placeholders(kv.Value)).Select(kv => kv.Key).Take(3).ToList();
        Check(lost.Count == 0, "Arabic keeps every {n} placeholder", string.Join(" · ", lost));
        var enPack = Pack("en");
        Check(enPack.Count == arPack.Count && enPack.Where(kv => Placeholders(kv.Key) != Placeholders(kv.Value)).Count() == 0,
            "English matches Arabic key for key", $"en={enPack.Count} ar={arPack.Count}");
        I18n.Init("ar");
        Check(I18n.T("Xuất cài đặt") == "تصدير الإعدادات" && I18n.T("bàn phím") == "لوحة المفاتيح",
            "newest strings are translated", I18n.T("Xuất cài đặt") + " / " + I18n.T("bàn phím"));
        Check(I18n.T("Thiếu tệp") == "ملف مفقود" && I18n.T("Lỗi mạng:") == "خطأ في الشبكة:",
            "error messages are translated too", I18n.T("Lỗi mạng:"));
        I18n.Init("en"); // các mục sau chạy bằng tiếng Anh như trước
    }

    static string Placeholders(string text) =>
        string.Join(",", System.Text.RegularExpressions.Regex.Matches(text, @"\{\d+}").Select(m => m.Value).OrderBy(x => x));

    /// <summary>Đọc thẳng tệp lang/&lt;mã&gt;.js (cùng định dạng I18n dùng) để kiểm tra toàn bộ gói, không chỉ vài câu mẫu.</summary>
    static Dictionary<string, string> Pack(string code)
    {
        try
        {
            var txt = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "lang", code + ".js"));
            var json = txt[txt.IndexOf('{')..(txt.LastIndexOf('}') + 1)];
            json = System.Text.RegularExpressions.Regex.Replace(json, @",\s*}$", "}");
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        }
        catch { return []; }
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
        /// <summary>Máy thứ ba "đứng" (không có khung 6 giây) để kiểm tra cách báo cáo.</summary>
        public bool AddStalled { get; init; }
        public IReadOnlyList<AiDeviceInfo> Devices() =>
        [
            new("d1", 1, "Phone One", "Pixel 7", "14", "online", "usb", 88, true, true, "192.168.1.20", ["Lab"], false, true, null),
            new("d2", 2, "Phone Two", "SM-G960F", "10", "offline", "wifi", null, null, false, "192.168.1.21", [], false, false, "offline"),
            .. AddStalled
                ? [new AiDeviceInfo("d3", 3, "Phone Three", "Redmi", "13", "online", "usb", 40, false, false, "", [], false, true, null,
                    Fps: 0, Kbps: 0, DecodeMs: 0, Dropped: 0, Plan: "480p/15fps/600kbps", Skipped: 12, AgeMs: 6000)]
                : Array.Empty<AiDeviceInfo>(),
        ];
        public IReadOnlyList<string> SelectedIds() => [];
        public void SetSelection(IEnumerable<string> ids) { }
        public string DeviceDetails(string id) => "#1 Phone One\nModel: Pixel 7";
        public string Diagnostics() => "ADB connections: 2";
        public string ReadLogs(int lines) => "log line";
        public string SettingsSnapshot() => "Grid: 480px";
        public string ApplySetting(string key, string value) => key is "thumbFps" or "theme" ? "OK" : "ERR unknown setting: " + key;
        public string SetQuality(string id, int maxSize, int fps, int kbps) => $"#{id} {maxSize}p/{fps}fps/{kbps}kbps";
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

    // ---------------- thứ tự ô tự xếp ----------------
    static void ManualOrderChecks()
    {
        var stored = new List<string> { "c", "a", "gone" };
        var norm = ManualOrder.Normalize(new[] { "a", "b", "c" }, stored);
        Check(norm.SequenceEqual(["c", "a", "b"]), "saved order wins, new phones go to the end", string.Join(",", norm));
        Check(!norm.Contains("gone"), "a phone that is gone is dropped from the order");
        Check(ManualOrder.Normalize(new[] { "a", "a", "b" }, ["a"]).SequenceEqual(["a", "b"]), "the order never duplicates a phone");
        Check(ManualOrder.Normalize(Array.Empty<string>(), stored).Count == 0, "an empty grid yields an empty order");

        var order = new List<string> { "a", "b", "c" };
        Check(ManualOrder.Move(order, "b", -1).SequenceEqual(["b", "a", "c"]), "moving earlier swaps with the phone before");
        Check(ManualOrder.Move(order, "b", 1).SequenceEqual(["a", "c", "b"]), "moving later swaps with the phone after");
        Check(ManualOrder.Move(order, "a", -1).SequenceEqual(["a", "b", "c"]), "the first phone stays first");
        Check(ManualOrder.Move(order, "c", 5).SequenceEqual(["a", "b", "c"]), "the last phone stays last");
        Check(ManualOrder.Move(order, "zz", 1).SequenceEqual(["a", "b", "c"]), "an unknown phone changes nothing");
        Check(order.SequenceEqual(["a", "b", "c"]), "moving returns a new list and leaves the input untouched");

        Check(ManualOrder.MovePast(["a", "b", "c"], "c", "a", -1).SequenceEqual(["c", "a", "b"]), "MovePast inserts before the neighbour");
        Check(ManualOrder.MovePast(["a", "b", "c"], "a", "c", 1).SequenceEqual(["b", "c", "a"]), "MovePast inserts after the neighbour");
        Check(ManualOrder.MovePast(["a", "b", "c"], "a", "zz", 1).SequenceEqual(["b", "c"]), "a missing neighbour leaves the phone out of the list");

        Check(Shortcuts.Match(Key.Left, ModifierKeys.Control | ModifierKeys.Shift) == Shortcuts.MoveEarlier, "Ctrl+Shift+Left moves the tile earlier");
        Check(Shortcuts.Match(Key.Right, ModifierKeys.Control | ModifierKeys.Shift) == Shortcuts.MoveLater, "Ctrl+Shift+Right moves the tile later");
        Check(Shortcuts.Match(Key.Left, ModifierKeys.Control) == null, "Ctrl+Left alone is not a shortcut");
        Check(!Shortcuts.WorksWhileTyping(Shortcuts.MoveEarlier), "moving a tile never steals keys from a text field");
        Check(Shortcuts.All.Select(x => x.Action).Distinct().Count() == Shortcuts.All.Length, "every shortcut action is unique");
    }

    // ---------------- gõ chữ vào điện thoại ----------------
    static void TypingChecks()
    {
        Check(Typing.IsPlainAscii("hello world"), "plain ASCII goes straight to the phone");
        Check(!Typing.IsPlainAscii("مرحبا"), "Arabic text needs the clipboard route", Typing.AdbArg("مرحبا"));
        Check(!Typing.IsPlainAscii("xin chào"), "Vietnamese diacritics need the clipboard route");
        Check(!Typing.IsPlainAscii("line1\nline2"), "multi-line text needs the clipboard route");
        Check(!Typing.IsPlainAscii(new string('a', Typing.MaxDirect + 1)), "very long text goes through the clipboard");
        Check(!Typing.IsPlainAscii(""), "empty text is not plain ASCII");
        Check(Typing.NeedsSession("مرحبا") && !Typing.NeedsSession("abc"), "only non-ASCII text needs a live session");
        Check(Typing.AdbArg("a b") == "a%sb", "spaces become %s for adb input text", Typing.AdbArg("a b"));
        Check(Typing.AdbArg("a;b|c&d") == "a\\;b\\|c\\&d", "shell metacharacters are escaped", Typing.AdbArg("a;b|c&d"));
        Check(Typing.AdbArg("it's") == "it\\'s", "a quote cannot break the adb command", Typing.AdbArg("it's"));
        Check(Typing.AdbArg("a\tb") == "a%sb", "tabs do not become control characters", Typing.AdbArg("a\tb"));
        Check(Typing.CanUseAdb("plain") && Typing.CanUseAdb("two\nlines") && !Typing.CanUseAdb("مرحبا"),
            "adb is only used for text it can actually type");
        Check(Typing.Preview("a\nb") == "a b", "previews stay on one line", Typing.Preview("a\nb"));
        var allOk = Typing.Summary("hi", [("#1", true, "via adb"), ("#2", true, "via adb")]);
        Check(allOk.StartsWith("OK:") && allOk.Contains("on 2/2 phone(s)"), "summary says OK only when every phone took the text", allOk.Split('\n')[0]);
        var mixed = Typing.Summary("مرحبا", [("#1", true, "qua phiên xem"), ("#2", false, "cần phiên xem")]);
        Check(mixed.StartsWith("PARTIAL:") && mixed.Contains("on 1/2 phone(s)"), "one phone short is PARTIAL, never OK", mixed.Split('\n')[0]);
        Check(mixed.Contains("FAIL #2"), "the summary names the phone that did not receive the text");
        Check(Typing.Summary("hi", [("#1", false, "offline")]).StartsWith("FAIL:"), "all phones failing is FAIL");
        Check(Typing.Summary("hi", []).StartsWith("FAIL:"), "an empty result set is FAIL, not OK");
        Check(Typing.Summary("a\nb", [("#1", true, "ok")]).Contains("\"a b\""), "the summary keeps the preview on one line");
        Check(Typing.Preview(new string('x', 50)).EndsWith("…") && Typing.Preview(new string('x', 50)).Length == 41,
            "long previews are cut with an ellipsis", Typing.Preview(new string('x', 50)));
    }

    // ---------------- điều khiển hàng đợi thao tác hàng loạt ----------------
    static void BatchChecks()
    {
        BatchTask New() => new()
        {
            Op = "shell",
            Title = "Kiểm tra",
            Total = 3,
            DeviceIds = ["a", "b", "c"],
            Payload = new() { ["cmd"] = "echo hi" },
        };

        var t = New();
        Check(!t.ShouldSkip("a") && t.CancelledCount == 0, "a fresh batch runs every phone");
        t.CancelDevice("b");
        Check(t.ShouldSkip("b") && !t.ShouldSkip("a") && !t.ShouldSkip("c"), "cancelling one phone leaves the others alone");
        Check(t.CancelledCount == 1 && t.CanStop, "cancelling one phone does not stop the whole batch");
        Check(t.CanStop && !t.Token.IsCancellationRequested && t.Skipped == 0, "batch can be stopped while running");
        t.Stop();
        Check(t.ShouldSkip("a") && t.ShouldSkip("c") && !t.CanStop, "stopping the batch skips everything left");
        Check(!t.CanStop && t.Token.IsCancellationRequested, "stop cancels the queue");
        Check(t.StatusText == I18n.T("Đang dừng…"), "stopping task says so", t.StatusText);
        Check(!t.CanStop, "stop is idempotent");
        var fin = New();
        fin.Finished = true;
        Check(!fin.CanStop && fin.StatusText == I18n.T("Xong · bấm để xem"), "finished task reports done", fin.StatusText);
        Check(!fin.CanStop && fin.Token.IsCancellationRequested == false, "finished task cannot be stopped");
        Check(new TaskResult("abc", 1, "máy", false, "", true).Skipped && !new TaskResult("abc", 1, "máy", true, "ok").Skipped,
            "skipped results are marked");

        var done = New();
        done.Finished = true;
        done.CancelDevice("a");
        Check(!done.IsCancelled("a"), "a finished batch ignores late cancellations");

        var r = New();
        r.Results.Add(new TaskResult("a", 1, "Máy A", true, "ok"));
        r.Results.Add(new TaskResult("b", 2, "Máy B", false, "lỗi"));
        r.Results.Add(new TaskResult("c", 3, "Máy C", false, "", true));
        Check(r.FailedIds.SequenceEqual(["b"]), "only real failures are retried, skipped phones are not", string.Join(",", r.FailedIds));
        r.Results.Add(new TaskResult("b", 2, "Máy B", false, "lỗi lần 2"));
        Check(r.FailedIds.SequenceEqual(["b"]), "a phone failing twice is retried once");
        var allOk = New();
        allOk.Results.Add(new TaskResult("a", 1, "Máy A", true, "ok"));
        Check(allOk.FailedIds.Count == 0, "a clean batch has nothing to retry");
        Check(r.Payload["cmd"] as string == "echo hi" && r.DeviceIds.Count == 3, "the batch keeps its payload and target list for a retry");
    }

    // ---------------- bảng phím tắt ----------------
    static void ShortcutChecks()
    {
        Check(Shortcuts.Match(System.Windows.Input.Key.F, System.Windows.Input.ModifierKeys.Control) == Shortcuts.FocusSearch, "ctrl+f focuses the search box");
        Check(Shortcuts.Match(System.Windows.Input.Key.A, System.Windows.Input.ModifierKeys.Control) == Shortcuts.SelectAll, "ctrl+a selects all phones");
        Check(Shortcuts.Match(System.Windows.Input.Key.A, System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift) == Shortcuts.AiChat, "ctrl+shift+a opens the assistant");
        Check(Shortcuts.Match(System.Windows.Input.Key.L, System.Windows.Input.ModifierKeys.Control) == Shortcuts.SoloSelection
              && Shortcuts.Match(System.Windows.Input.Key.L, System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift) == Shortcuts.ShowAll,
            "ctrl+l and ctrl+shift+l are distinct");
        Check(Shortcuts.Match(System.Windows.Input.Key.F5, System.Windows.Input.ModifierKeys.None) == Shortcuts.RefreshInfo, "f5 refreshes device info");
        Check(Shortcuts.Match(System.Windows.Input.Key.Escape, System.Windows.Input.ModifierKeys.None) == Shortcuts.Escape, "escape clears search or selection");
        Check(Shortcuts.Match(System.Windows.Input.Key.F1, System.Windows.Input.ModifierKeys.None) == Shortcuts.Help, "f1 opens the shortcut list");
        Check(Shortcuts.Match(System.Windows.Input.Key.F, System.Windows.Input.ModifierKeys.None) == null, "a bare letter does not trigger a shortcut");
        Check(Shortcuts.Match(System.Windows.Input.Key.F, System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Alt) == null, "extra modifiers do not match");
        Check(Shortcuts.Match(System.Windows.Input.Key.F, System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Windows) == Shortcuts.FocusSearch, "the windows key is ignored");
        Check(Shortcuts.All.Select(x => x.Action).Distinct().Count() == Shortcuts.All.Length, "every shortcut action is unique");
        Check(Shortcuts.All.All(x => !string.IsNullOrWhiteSpace(x.Label)), "every shortcut has a label for the help dialog");
        var ai = Shortcuts.All.First(x => x.Action == Shortcuts.AiChat);
        Check(Shortcuts.Gesture(ai) == "Ctrl+Shift+A", "gesture text is readable", Shortcuts.Gesture(ai));
        Check(Shortcuts.Gesture(Shortcuts.All.First(x => x.Action == Shortcuts.Escape)) == "Esc", "escape gesture is spelled Esc");
        Check(Shortcuts.WorksWhileTyping(Shortcuts.Escape) && Shortcuts.WorksWhileTyping(Shortcuts.Help)
              && !Shortcuts.WorksWhileTyping(Shortcuts.SelectAll), "typing in a text box is never hijacked");
    }

    // ---------------- sao lưu / khôi phục toàn bộ dữ liệu người dùng ----------------
    static void BackupChecks()
    {
        var doc = new UserBackup.Doc
        {
            Devices = new()
            {
                ["SER1"] = new DeviceMeta { Label = "Máy A", Num = 3, Groups = ["nhóm 1"] },
                ["192.168.1.9:5555"] = new DeviceMeta { Label = "Máy B", Num = 4 },
            },
            Groups = ["nhóm 1", "nhóm 2"],
            NextNum = 5,
            Theme = "light",
            Lang = "ar",
            TileW = 260,
            Sort = "num",
            HideOffline = true,
            Ai = new AiSettings { MaxToolRounds = 5, Stream = false, ExtraSystemPrompt = "ngắn gọn" },
        };
        var chats = new[] { ("chat-1", "{\"Id\":\"chat-1\"}"), ("chat 2", "{\"Id\":\"chat 2\"}") };
        var bytes = UserBackup.Build(doc, chats);
        var info = UserBackup.Read(bytes);

        Check(info.Manifest.Devices == 2 && info.Manifest.Groups == 2 && info.Manifest.Chats == 2, "backup manifest counts devices, groups and chats");
        Check(!info.Manifest.ApiKeys, "backup manifest states that no API key is included");
        Check(info.Doc.Devices["SER1"].Label == "Máy A" && info.Doc.Devices["SER1"].Num == 3, "device names survive a backup round trip");
        Check(info.Doc.Devices.ContainsKey("192.168.1.9:5555"), "wifi serial keys survive a backup round trip");
        Check(info.Doc.Groups.Count == 2 && info.Doc.Groups[1] == "nhóm 2", "groups survive a backup round trip");
        Check(info.Doc.Theme == "light" && info.Doc.Lang == "ar" && info.Doc.TileW == 260 && info.Doc.HideOffline == true, "ui prefs survive a backup round trip");
        Check(info.Doc.Ai?.MaxToolRounds == 5 && info.Doc.Ai?.Stream == false, "ai settings survive a backup round trip");
        Check(info.ChatIds.Count == 2 && UserBackup.ReadChats(bytes).Values.Any(v => v.Contains("chat 2")), "chat history is stored and read back", string.Join(",", info.ChatIds));
        var raw = System.Text.Encoding.UTF8.GetString(bytes);
        Check(!raw.Contains("KeyProtected") && !raw.Contains("sk-"), "backup never contains api keys");
        using (var ms = new MemoryStream(bytes))
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read))
            Check(zip.GetEntry("backup.json") != null && zip.GetEntry("data.json") != null && zip.Entries.Count == 4,
                "backup zip layout is manifest + data + one entry per chat", zip.Entries.Count + " entries");

        var target = new StoreState { NextNum = 2 };
        target.Devices["SER1"] = new DeviceMeta { Label = "cũ", Num = 3 };
        target.Devices["LOCAL"] = new DeviceMeta { Label = "máy mới", Num = 7 };
        target.Groups.Add("nhóm 1");
        var (added, newGroups) = UserBackup.MergeInto(target, info.Doc);
        Check(added == 1 && target.Devices["LOCAL"].Label == "máy mới", "restore adds missing devices and keeps local ones", added + " added");
        Check(target.Devices["SER1"].Label == "Máy A", "restore replaces the metadata of a matching serial");
        Check(newGroups == 1 && target.Groups.Count == 2, "restore merges groups without duplicates");
        Check(target.NextNum == 5, "restore never lowers the next device number");
        Check(UserBackup.MissingChats(["a", "b"], id => id == "a").SequenceEqual(["b"]), "restore skips conversations that already exist");

        var rejected = false;
        try { UserBackup.Read(System.Text.Encoding.UTF8.GetBytes("not a zip")); } catch (Exception) { rejected = true; }
        Check(rejected, "a file that is not a zip is rejected");

        var foreign = false;
        using (var ms = new MemoryStream())
        {
            using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
            {
                var e = zip.CreateEntry("backup.json");
                using var w = new StreamWriter(e.Open());
                w.Write("{\"App\":\"OtherApp\",\"Version\":1}");
            }
            try { UserBackup.Read(ms.ToArray()); } catch (InvalidDataException) { foreign = true; }
        }
        Check(foreign, "a backup from another application is rejected");
    }

    // ---------------- phát trực tiếp câu trả lời của trợ lý (SSE, không cần mạng) ----------------
    static void StreamChecks()
    {
        var parser = new Ai.SseParser();
        var two = parser.Feed("data: {\"a\":1}\n\ndata: {\"b\":2}\n\nda");
        Check(two.Count == 2 && two[0].Data == "{\"a\":1}" && two[1].Data == "{\"b\":2}", "sse splits complete events", two.Count + " events");
        var joined = parser.Feed("ta: {\"c\":3}\n\n");
        Check(joined.Count == 1 && joined[0].Data == "{\"c\":3}", "sse joins a chunk cut in the middle");

        var crlf = new Ai.SseParser();
        var named = crlf.Feed("event: ping\r\ndata: x\r\n\r\n");
        Check(named.Count == 1 && named[0].Name == "ping" && named[0].Data == "x", "sse handles CRLF and event names");

        var cmt = new Ai.SseParser();
        Check(cmt.Feed(": keep-alive\n\ndata: y\n\n").Count == 1, "sse ignores comments");
        var tail = new Ai.SseParser();
        Check(tail.Feed("data: z").Count == 0 && tail.Flush().Count == 1, "sse flush emits the last event");

        var oa = new Ai.StreamAccumulator(Ai.AiProviderKind.OpenAi);
        var d1 = oa.Feed(new Ai.SseEvent("", "{\"choices\":[{\"delta\":{\"content\":\"Hel\"}}]}"));
        var d2 = oa.Feed(new Ai.SseEvent("", "{\"choices\":[{\"delta\":{\"content\":\"lo\"}}]}"));
        oa.Feed(new Ai.SseEvent("", "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_1\",\"function\":{\"name\":\"list_devices\",\"arguments\":\"{\\\"a\\\":\"}}]}}]}"));
        oa.Feed(new Ai.SseEvent("", "{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"1}\"}}]}}]}"));
        oa.Feed(new Ai.SseEvent("", "{\"choices\":[],\"usage\":{\"prompt_tokens\":11,\"completion_tokens\":7}}"));
        var r1 = oa.Result();
        Check(d1 == "Hel" && d2 == "lo", "openai deltas are returned for display");
        Check(r1.Text == "Hello", "openai text accumulates", r1.Text);
        Check(r1.ToolCalls.Count == 1 && r1.ToolCalls[0].Name == "list_devices" && r1.ToolCalls[0].Id == "call_1", "openai tool call merged from deltas");
        Check(r1.ToolCalls.Count == 1 && r1.ToolCalls[0].ArgumentsJson == "{\"a\":1}", "openai tool arguments merged", r1.ToolCalls.Count == 1 ? r1.ToolCalls[0].ArgumentsJson : "");
        Check(r1.Usage.InputTokens == 11 && r1.Usage.OutputTokens == 7, "openai usage captured");

        var an = new Ai.StreamAccumulator(Ai.AiProviderKind.Anthropic);
        an.Feed(new Ai.SseEvent("message_start", "{\"type\":\"message_start\",\"message\":{\"usage\":{\"input_tokens\":21}}}"));
        var t1 = an.Feed(new Ai.SseEvent("content_block_delta", "{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"مرحبا\"}}"));
        an.Feed(new Ai.SseEvent("content_block_start", "{\"type\":\"content_block_start\",\"index\":1,\"content_block\":{\"type\":\"tool_use\",\"id\":\"toolu_9\",\"name\":\"list_devices\"}}"));
        an.Feed(new Ai.SseEvent("content_block_delta", "{\"type\":\"content_block_delta\",\"index\":1,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"{\\\"x\\\":2}\"}}"));
        an.Feed(new Ai.SseEvent("message_delta", "{\"type\":\"message_delta\",\"usage\":{\"output_tokens\":13}}"));
        var r2 = an.Result();
        Check(t1 == "مرحبا" && r2.Text == "مرحبا", "anthropic text delta shown and accumulated", r2.Text);
        Check(r2.ToolCalls.Count == 1 && r2.ToolCalls[0].Id == "toolu_9" && r2.ToolCalls[0].ArgumentsJson == "{\"x\":2}", "anthropic tool input merged", r2.ToolCalls.Count == 1 ? r2.ToolCalls[0].ArgumentsJson : "");
        Check(r2.Usage.InputTokens == 21 && r2.Usage.OutputTokens == 13, "anthropic usage captured");

        var gm = new Ai.StreamAccumulator(Ai.AiProviderKind.Gemini);
        var g1 = gm.Feed(new Ai.SseEvent("", "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Hi\"}]}}]}"));
        gm.Feed(new Ai.SseEvent("", "{\"candidates\":[{\"content\":{\"parts\":[{\"functionCall\":{\"name\":\"list_devices\",\"args\":{\"k\":1}}}]}}]}"));
        gm.Feed(new Ai.SseEvent("", "{\"usageMetadata\":{\"promptTokenCount\":5,\"candidatesTokenCount\":3}}"));
        var r3 = gm.Result();
        Check(g1 == "Hi" && r3.Text == "Hi", "gemini text part shown and accumulated");
        Check(r3.ToolCalls.Count == 1 && r3.ToolCalls[0].Name == "list_devices", "gemini functionCall captured");
        Check(r3.Usage.Total == 8, "gemini usage captured");

        var openai = new Ai.AiProvider { Kind = Ai.AiProviderKind.OpenAi, BaseUrl = "https://api.openai.com/v1", Model = "gpt-4o-mini" };
        var body = Ai.AiClient.StreamBody(openai, "{\"model\":\"gpt-4o-mini\",\"messages\":[]}");
        Check(body.Contains("\"stream\":true") && body.Contains("include_usage"), "openai stream body asks for usage", body);
        var gemini = new Ai.AiProvider { Kind = Ai.AiProviderKind.Gemini, BaseUrl = "https://generativelanguage.googleapis.com", Model = "gemini-2.5-flash" };
        Check(Ai.AiClient.StreamUrl(gemini).Contains(":streamGenerateContent?alt=sse"), "gemini uses the streaming endpoint", Ai.AiClient.StreamUrl(gemini));
        Check(Ai.AiClient.StreamBody(gemini, "{\"a\":1}") == "{\"a\":1}", "gemini body unchanged (sse is a URL parameter)");
        var anth = new Ai.AiProvider { Kind = Ai.AiProviderKind.Anthropic, BaseUrl = "https://api.anthropic.com", Model = "claude-sonnet-4-5" };
        Check(Ai.AiClient.StreamBody(anth, "{\"x\":1}").Contains("\"stream\":true") && Ai.AiClient.StreamUrl(anth).EndsWith("/v1/messages"), "anthropic stream body + url");
        Check(new Ai.StreamAccumulator(Ai.AiProviderKind.OpenAi).Empty, "empty stream is detected so the caller can fall back");

        // Luồng vỡ giữa chừng: chữ đã nhận phải được giữ lại (không gửi lại cả câu = tốn tiền, chữ trên màn hình biến mất),
        // nhưng lời gọi công cụ nhận dở phải bị bỏ vì JSON của nó có thể đã bị cắt.
        var broken = new Ai.StreamAccumulator(Ai.AiProviderKind.OpenAi);
        foreach (var ev in new Ai.SseParser().Feed(
            "data: {\"choices\":[{\"delta\":{\"content\":\"Đang kiểm tra\"}}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"name\":\"tap\",\"arguments\":\"{\\\"x\\\"\"}}]}}]}\n\n"))
            broken.Feed(ev);
        Check(!broken.Empty && broken.HasText, "a broken stream still counts as content");
        var partialReply = broken.Partial();
        Check(partialReply is { Incomplete: true } && partialReply.Text == "Đang kiểm tra",
            "a reply cut mid-stream keeps the text it already showed", partialReply?.Text ?? "null");
        Check(partialReply != null && partialReply.ToolCalls.Count == 0 && partialReply.Ok,
            "a half-received tool call is dropped, not executed", partialReply?.ToolCalls.Count.ToString() ?? "null");
        Check(new Ai.StreamAccumulator(Ai.AiProviderKind.OpenAi).Partial() == null,
            "a stream that broke before any text still falls back to the plain call");
        Check(new Ai.AiReply { Text = "x", Incomplete = true }.Incomplete && new Ai.AiReply { Text = "x" }.Ok,
            "a complete reply is not marked incomplete");
    }

    // ---------------- tự phục hồi luồng lưới ----------------
    static void WatchdogChecks()
    {
        // khung vẫn chảy: không làm gì
        Check(StreamWatchdog.Decide(true, true, 200, 0, 0) == RecoveryStep.None, "a live stream needs no recovery");
        // chưa tới ngưỡng 2,5 giây
        Check(StreamWatchdog.Decide(true, true, 2400, 0, long.MaxValue) == RecoveryStep.None, "a short gap is not a stall", "2400 ms");
        // đứng quá 2,5 giây, chưa xin lần nào → xin keyframe
        Check(StreamWatchdog.Decide(true, true, 2600, 0, 0) == RecoveryStep.KeyFrame, "a 2.6 s gap asks for a keyframe");
        // đã xin keyframe, còn trong 6 giây → chờ
        Check(StreamWatchdog.Decide(true, true, 3000, 1, 0) == RecoveryStep.Wait, "after asking, it waits before pushing harder");
        // quá 6 giây và còn lượt → đánh thức bộ giải mã
        Check(StreamWatchdog.Decide(true, true, 6500, 1, 0) == RecoveryStep.Reset, "a 6.5 s gap wakes the decoder");
        // hết lượt (3) và chưa từng dựng lại → dựng lại phiên
        Check(StreamWatchdog.Decide(true, true, 9000, 3, long.MaxValue) == RecoveryStep.Restart, "after three attempts the session is rebuilt");
        // vừa dựng lại 10 giây trước → không đập lại
        Check(StreamWatchdog.Decide(true, true, 9000, 3, 10_000) == RecoveryStep.Wait, "no rebuild within the one-minute cooldown");
        // đủ một phút → được phép dựng lại
        Check(StreamWatchdog.Decide(true, true, 90_000, 6, StreamWatchdog.RestartCooldownMs) == RecoveryStep.Restart, "the cooldown expires after a minute");
        // máy mất kết nối / ô không có hình: không phải việc của bộ theo dõi khung
        Check(StreamWatchdog.Decide(false, true, 9999, 9, long.MaxValue) == RecoveryStep.Wait, "an offline phone is not a stalled stream");
        Check(StreamWatchdog.Decide(true, false, 9999, 9, long.MaxValue) == RecoveryStep.Wait, "a control-only tile has no frames to stall");
        Check(StreamWatchdog.RestartCooldownMs >= 30_000 && StreamWatchdog.MaxAttempts >= 2,
            "recovery is patient enough not to hammer a broken phone", $"{StreamWatchdog.MaxAttempts} attempts, {StreamWatchdog.RestartCooldownMs / 1000} s");
    }

    // ---------------- nhật ký riêng của từng máy ----------------
    static void DeviceLogChecks()
    {
        const string a = "selfcheck-a", b = "selfcheck-b";
        try
        {
            DeviceLog.Clear(a);
            DeviceLog.Clear(b);
            DeviceLog.Add(a, "status offline → connecting");
            DeviceLog.Add(a, "status connecting → online");
            DeviceLog.Add(b, "status offline → error · mất kết nối", error: true);
            Check(DeviceLog.Tail(a, 50).Count == 2, "device log keeps entries per phone", DeviceLog.Tail(a, 50).Count + " entries");
            Check(DeviceLog.Tail(b, 50).Count == 1, "entries do not leak between phones");
            Check(DeviceLog.Tail(a, 50)[^1].Text.Contains("online") && !DeviceLog.Tail(a, 50)[^1].Error, "latest entry last, plain entry not an error");
            Check(DeviceLog.Tail(b, 50)[0].Error, "error entries are flagged");
            Check(DeviceLog.Format(a, 10).Contains("→ online"), "formatted log contains the transition");
            Check(File.Exists(DeviceLog.PathFor(a)) && File.ReadAllText(DeviceLog.PathFor(a)).Contains("→ online"), "log is appended to its own file");

            for (var i = 0; i < 320; i++) DeviceLog.Add(a, "bulk " + i);
            Check(DeviceLog.Tail(a, 1000).Count == 300, "in-memory log is capped at 300 lines", DeviceLog.Tail(a, 1000).Count + " lines");

            Check(DeviceLog.SafeName("192.168.1.5:5555") == "192.168.1.5_5555", "wifi serial is made file-safe", DeviceLog.SafeName("192.168.1.5:5555"));
            Check(DeviceLog.SafeName("") == "device" && !DeviceLog.SafeName("..//").Contains('/'), "empty or hostile ids get a safe name");

            var rot = Path.Combine(DeviceLog.Dir, "rotate-test.log");
            Directory.CreateDirectory(DeviceLog.Dir);
            File.WriteAllText(rot, new string('x', (int)DeviceLog.MaxFileBytes + 10));
            DeviceLog.Rotate(rot);
            Check(!File.Exists(rot) && File.Exists(rot + ".1"), "large log is rotated to .1");
            File.Delete(rot + ".1");

            DeviceLog.Clear(a);
            Check(DeviceLog.Tail(a, 10).Count == 0 && !File.Exists(DeviceLog.PathFor(a)), "clearing removes memory and file");
        }
        finally
        {
            DeviceLog.Clear(a);
            DeviceLog.Clear(b);
            try { File.Delete(Path.Combine(DeviceLog.Dir, "rotate-test.log.1")); } catch { }
        }
    }

    // ---------------- chế độ an toàn khi khởi động hỏng liên tiếp ----------------
    static void SafeModeChecks()
    {
        Check(SessionGuard.FailsAfterStart(null) == 0, "first run is not a failure");
        Check(SessionGuard.FailsAfterStart(new SessionState { LastStart = "t", LastReady = "t", Fails = 3 }) == 0, "graceful last run resets the counter");
        Check(SessionGuard.FailsAfterStart(new SessionState { LastStart = "t", LastReady = null, Fails = 0 }) == 1, "unfinished last run counts");
        Check(SessionGuard.FailsAfterStart(new SessionState { LastStart = "t", LastReady = null, Fails = 4 }) == 5, "counter keeps growing");
        Check(!SessionGuard.ShouldUseSafeMode(1) && SessionGuard.ShouldUseSafeMode(2) && SessionGuard.ShouldUseSafeMode(7), "two unfinished starts enable safe mode");

        var dir = Path.Combine(Path.GetTempPath(), "cp-sess-" + Guid.NewGuid().ToString("N")[..8]);
        var path = Path.Combine(dir, "session.json");
        try
        {
            Check(SessionGuard.Begin(path) == 0, "begin on a clean folder");
            Check(SessionGuard.Begin(path) == 1, "begin again without ready counts one failure");
            Check(SessionGuard.Begin(path) == 2, "and another one");
            Check(SessionGuard.ShouldUseSafeMode(SessionGuard.Begin(path)), "safe mode offered after repeated failures");
            SessionGuard.Ready(path);
            Check(SessionGuard.Begin(path) == 0, "ready resets the counter");
            SessionGuard.Clean(path);
            Check(SessionGuard.Read(path)?.Fails == 0, "clean leaves a healthy state");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }

        var heavy = new Settings { ThumbMaxSize = 720, ThumbFps = 30, ThumbBitRate = 2_000_000, HqMaxSize = 1920, HqFps = 60, HqBitRate = 8_000_000 };
        var light = new Settings { ThumbMaxSize = 160, ThumbFps = 4, ThumbBitRate = 200_000, HqMaxSize = 640, HqFps = 10, HqBitRate = 800_000 };
        var before = DeviceVideo.Thumb(new DeviceMeta { ThumbMaxSize = 1080, ThumbFps = 60 }, heavy);
        Check(before.MaxSize == 1080 && before.MaxFps == 60, "without safe mode the per-phone values apply", before.ToString());

        SafeMode.Activate(3);
        try
        {
            var safeThumb = DeviceVideo.Thumb(new DeviceMeta { ThumbMaxSize = 1080, ThumbFps = 60 }, heavy);
            Check(safeThumb == new VideoPlan(SafeMode.LightMaxSize, SafeMode.LightBitRate, SafeMode.LightFps), "safe mode caps the grid stream", safeThumb.ToString());
            var safeHq = DeviceVideo.Hq(null, heavy);
            Check(safeHq == new VideoPlan(SafeMode.LightHqMaxSize, SafeMode.LightHqBitRate, SafeMode.LightHqFps), "safe mode caps the viewer stream", safeHq.ToString());
            var alreadyLight = DeviceVideo.Thumb(null, light);
            Check(alreadyLight == new VideoPlan(160, 200_000, 4), "safe mode never makes a light profile heavier", alreadyLight.ToString());
            Check(DeviceVideo.Thumb(new DeviceMeta { ThumbMaxSize = 1080 }, heavy).MaxSize <= SafeMode.LightMaxSize, "per-phone override ignored in safe mode");
        }
        finally { SafeMode.Reset(); }

        Check(!SafeMode.Active && DeviceVideo.Thumb(null, heavy) == new VideoPlan(720, 2_000_000, 30), "leaving safe mode restores the user profile");
        Check(SafeMode.Light(0, 240) == 240 && SafeMode.Light(120, 240) == 120 && SafeMode.Light(1000, 240) == 240, "light caps are one-way");
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

            // Gói chẩn đoán phải mang đúng tệp tuỳ chọn giao diện mà ứng dụng thật sự ghi (native-ui.json). Bản cũ chép
            // "ui.json" — tệp không tồn tại — nên gói gửi cho hỗ trợ thiếu theme/ngôn ngữ/RTL mà README vẫn hứa có.
            var data = Path.Combine(dir, "data");
            Directory.CreateDirectory(data);
            File.WriteAllText(Path.Combine(data, "config.json"),
                "{\"devices\":{\"SER1\":{\"label\":\"Máy A\"}},\"settings\":{\"note\":\"sk-live-ABCDEFGH1234\"}}");
            File.WriteAllText(Path.Combine(data, "native-ui.json"),
                "{\"theme\":\"light\",\"lang\":\"ar\",\"tileW\":240,\"sort\":\"name\",\"filter\":\"group:A\",\"skipVersion\":\"v9.9.9\"}");
            var bundle = DiagBundle.Build(zip, ["#1\tMáy A\tusb\tonline\tAndroid 13"], 20, data);
            var names = DiagBundle.Entries(bundle);
            Check(names.Contains("settings/native-ui.json"), "bundle carries the real UI prefs file", string.Join(",", names.Where(n => n.StartsWith("settings/"))));
            Check(!names.Contains("settings/ui.json"), "bundle no longer pretends ui.json exists");
            var ui = ReadEntry(bundle, "settings/native-ui.json");
            Check(ui.Contains("\"light\"") && ui.Contains("\"ar\"") && ui.Contains("group:A") && ui.Contains("v9.9.9"),
                "theme, language, filter and skipped version survive into the bundle");
            var cfg = ReadEntry(bundle, "settings/config.json");
            Check(cfg.Contains("Máy A") && !cfg.Contains("sk-live-ABCDEFGH1234") && cfg.Contains("••••"), "config is included with keys masked");
            var readme = ReadEntry(bundle, "README.txt");
            var listed = readme.Split('\n')
                .Select(l => System.Text.RegularExpressions.Regex.Match(l, @"^  ([A-Za-z0-9_./\-]+\.(?:txt|json|log))"))
                .Where(m => m.Success).Select(m => m.Groups[1].Value).ToList();
            Check(listed.Contains("settings/native-ui.json") && !listed.Contains("settings/ui.json") && !listed.Contains("ai.json"),
                "README lists the UI prefs file and never ui.json or ai.json", string.Join(",", listed));
            Check(listed.Count == names.Count(n => n != "README.txt") && names.Where(n => n != "README.txt").All(listed.Contains),
                "README lists exactly the entries in the bundle", string.Join(",", listed));
            File.Delete(Path.Combine(data, "native-ui.json"));
            var bare = DiagBundle.Entries(DiagBundle.Build(zip, null, 5, data));
            Check(!bare.Contains("settings/native-ui.json") && !ReadEntry(zip, "README.txt").Contains("settings/native-ui.json"),
                "a missing UI file is neither copied nor promised");
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

        // 3.15.0 trở đi mỗi bản có ba tệp: bộ cài, ZIP chạy được và manifests.zip (vài KB). Chọn nhầm tệp cuối là một
        // nút "Tải ZIP" trả về YAML thay vì ứng dụng — đúng lỗi mà bộ chọn tệp dưới đây phải chặn.
        var three = new (string, long)[]
        {
            ("ControlPhone-Setup-v99.0.0-win64.exe", 60483164),
            ("ControlPhone-v99.0.0-manifests.zip", 2630),
            ("ControlPhone-v99.0.0-win64.zip", 84228493),
        };
        var picked = Updates.PickAssets(three);
        Check(picked.ZipName == "ControlPhone-v99.0.0-win64.zip" && picked.ZipBytes == 84228493, "the portable ZIP is picked, not the manifests archive", picked.ZipName);
        Check(picked.SetupName == "ControlPhone-Setup-v99.0.0-win64.exe" && picked.HasSetup, "the installer is picked", picked.SetupName);
        Check(Updates.PickAssets(three.Reverse()).ZipName == picked.ZipName, "asset order from the API does not matter");
        Check(Updates.PickAssets([("ControlPhone-v99.0.0-manifests.zip", 99999999), ("ControlPhone-v99.0.0-win64.zip", 1000)]).ZipName.EndsWith("-win64.zip"),
            "a bigger manifests archive never wins");
        Check(Updates.PickAssets([("ControlPhone-v99.0.0-manifests.zip", 2630)]).HasZip == false, "manifests alone is not a portable package");
        Check(Updates.PickAssets([("ControlPhone-v99.0.0-symbols.zip", 500), ("ControlPhone.zip", 700)]).ZipName == "ControlPhone.zip", "symbols skipped, plain zip accepted");
        Check(Updates.PickAssets([("portable.zip", 10), ("ControlPhone-Setup-v99.0.0-win64.exe", 20)]).HasZip, "zip without the win64 suffix still counts");
        Check(Updates.PickAssets([("ControlPhone-Setup-v99.0.0-win64.exe", 0), ("ControlPhone-v99.0.0-win64.zip", 0)]).HasSetup == false,
            "zero-size assets are not offered (an upload in progress is a dead link)");
        Check(Updates.PickAssets([]).HasZip == false && Updates.PickAssets([]).HasSetup == false, "no assets means no buttons");
        {
            const string threeJson = "{\"tag_name\":\"v99.0.0\",\"assets\":["
                + "{\"name\":\"ControlPhone-v99.0.0-manifests.zip\",\"size\":2630},"
                + "{\"name\":\"ControlPhone-v99.0.0-win64.zip\",\"size\":84228493},"
                + "{\"name\":\"ControlPhone-Setup-v99.0.0-win64.exe\",\"size\":60483164}]}";
            var r3 = Updates.Parse(threeJson);
            Check(r3?.ZipUrl.EndsWith("/ControlPhone-v99.0.0-win64.zip") == true && r3.ZipBytes == 84228493, "parse keeps the runnable ZIP with three assets", r3?.ZipName);
        }

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
    // ---------------- bàn phím PC: khôi phục bàn phím điện thoại ----------------
    static void KeyboardChecks()
    {
        const string gboard = "com.google.android.inputmethod.latin/com.android.inputmethod.latin.LatinIME";
        const string samsung = "com.samsung.android.honeyboard/.service.HoneyBoardService";
        const string voice = "com.google.android.googlequicksearchbox/com.google.android.voicesearch.Ime";
        var ours = PcKeyboard.Ime;

        var order = PcKeyboard.RestoreOrder(samsung, [gboard, samsung]);
        Check(order[0] == samsung && order.Count == 2, "the remembered keyboard is restored first", string.Join(" > ", order));

        // Người dùng gỡ bàn phím đã lưu: "ime set" thất bại im lặng và điện thoại ở lại với bàn phím không hiện trên màn hình.
        var gone = PcKeyboard.RestoreOrder("com.example.uninstalled/.Ime", [gboard]);
        Check(gone.Count == 1 && gone[0] == gboard, "a keyboard that no longer exists is not attempted", string.Join(" > ", gone));

        Check(!PcKeyboard.RestoreOrder("null", [gboard]).Contains("null"), "'null' from settings is not a keyboard");
        Check(!PcKeyboard.RestoreOrder(ours, [gboard, ours]).Contains(ours), "our own keyboard is never a restore target");
        Check(PcKeyboard.RestoreOrder(null, [voice, gboard]).SequenceEqual([gboard, voice]), "a voice keyboard is the last resort");
        Check(PcKeyboard.RestoreOrder(null, [gboard, gboard.ToUpperInvariant()]).Count == 1, "duplicates are removed regardless of case");
        Check(PcKeyboard.RestoreOrder(null, []).Count == 0, "no other keyboard means an empty plan");
        Check(PcKeyboard.RestoreOrder(null, [voice]).SequenceEqual([voice]), "voice is still better than no keyboard");
        Check(PcKeyboard.RestoreOrder("  " + samsung + " ", [samsung, gboard])[0] == samsung, "whitespace around the saved name is ignored");
    }

    // ---------------- chọn máy cho trợ lý AI: mơ hồ thì từ chối, không đoán ----------------
    static void TargetChecks()
    {
        static Ai.AiToolHost.PhoneTarget T(string id, int num, string name) => new(id, num, name, "online");
        var phones = new List<Ai.AiToolHost.PhoneTarget>
        {
            T("SER1", 1, "Pixel 8"), T("SER2", 2, "Pixel 8 Pro"), T("SER3", 3, "Galaxy A54"), T("SER4", 4, "Máy A"),
        };
        var (hit, why) = Ai.AiToolHost.MatchOne(phones, "SER3");
        Check(hit?.Num == 3, "a serial resolves the phone", why);
        Check(Ai.AiToolHost.MatchOne(phones, "#2").Hit?.Num == 2 && Ai.AiToolHost.MatchOne(phones, "2").Hit?.Num == 2,
            "a number with or without '#' resolves");
        Check(Ai.AiToolHost.MatchOne(phones, "galaxy a54").Hit?.Num == 3, "a name is case-insensitive");

        // "Pixel" khớp 2 máy: chọn máy đầu tiên là gửi lệnh xoá dữ liệu/gỡ ứng dụng lên máy người dùng không chỉ định.
        var (ambiguous, why2) = Ai.AiToolHost.MatchOne(phones, "Pixel");
        Check(ambiguous == null && why2.Contains("matches 2 phones"), "an ambiguous name is refused, not guessed", why2);
        var (unknown, why3) = Ai.AiToolHost.MatchOne(phones, "iPhone");
        Check(unknown == null && why3.Contains("nothing matches"), "an unknown name is reported", why3);
        var (missing, why4) = Ai.AiToolHost.MatchOne(phones, "9");
        Check(missing == null && why4.Contains("no phone has the number"), "a number that does not exist is reported", why4);
        Check(Ai.AiToolHost.MatchOne(phones, "  ").Hit == null, "an empty entry is refused");

        // Hai máy trùng tên: tên chính xác cũng không đủ để chọn.
        var twins = new List<Ai.AiToolHost.PhoneTarget> { T("A", 1, "Máy A"), T("B", 2, "Máy A") };
        var (twin, why5) = Ai.AiToolHost.MatchOne(twins, "Máy A");
        Check(twin == null && why5.Contains("2 phones are called"), "duplicate names are refused", why5);

        // Một phần tên chỉ khớp một máy thì vẫn tiện: "Galaxy" → #3.
        Check(Ai.AiToolHost.MatchOne(phones, "Galaxy").Hit?.Num == 3, "a unique partial name still resolves");
        Check(Ai.AiToolHost.MatchOne(phones, "Máy A").Hit?.Num == 4, "a Vietnamese name resolves");
    }

    // ---------------- hiệu năng: số đo luồng, điều chỉnh thích ứng, chẩn đoán ----------------
    static void PerfChecks()
    {
        // ---- 1) số đo luồng ----
        var st = new Video.StreamStats();
        for (int i = 0; i <= 10; i++) st.Frame(480, 1080, 4.0, i * 100);
        var snap = st.Snapshot(1000);
        Check(Math.Abs(snap.Fps - 11) < 0.5, "fps is measured over a one second window", $"{snap.Fps:0.00}");
        Check(snap.Width == 480 && snap.Height == 1080, "frame size is remembered", $"{snap.Width}x{snap.Height}");

        var bw = new Video.StreamStats();
        bw.Bytes(2000, 0);          // mốc đầu
        bw.Bytes(2000, 1000);       // 2000 byte trong 1000 ms = 16 kbit/s
        Check(Math.Abs(bw.Snapshot(1000).Kbps - 16) < 0.5, "bitrate follows the bytes received", $"{bw.Snapshot(1000).Kbps:0.0}");

        var ewma = new Video.StreamStats();
        ewma.Frame(10, 10, 10, 0);
        ewma.Frame(10, 10, 50, 100);
        Check(Math.Abs(ewma.Snapshot(200).DecodeMs - 18) < 0.01, "decode time is smoothed, not spiky", $"{ewma.Snapshot(200).DecodeMs:0.00}");

        var drop = new Video.StreamStats();
        drop.Dropped(); drop.Dropped(2);
        Check(drop.Snapshot(0).Dropped == 3, "dropped frames are counted");

        var silent = new Video.StreamStats();
        silent.Bytes(4000, 0);
        silent.Bytes(4000, 1000);
        silent.Frame(480, 800, 3, 1000);
        Check(Math.Abs(silent.Snapshot(1500).Kbps - 32) < 0.5, "bitrate is reported while bytes arrive", $"{silent.Snapshot(1500).Kbps:0.0}");
        Check(silent.Snapshot(3000).Kbps == 0, "a silent stream stops claiming bandwidth", $"{silent.Snapshot(3000).Kbps:0.0}");
        Check(Math.Abs(silent.Snapshot(4000).AgeMs - 3000) < 1, "the age of the last frame is reported", $"{silent.Snapshot(4000).AgeMs:0}");

        var kinds = new Video.StreamStats();
        kinds.Skipped(4);
        kinds.Dropped(2);
        var kindSnap = kinds.Snapshot(0);
        Check(kindSnap.Skipped == 4 && kindSnap.Dropped == 2, "key-frame skips are counted apart from drops",
            $"skip {kindSnap.Skipped} drop {kindSnap.Dropped}");

        // ---- 2) bộ điều chỉnh thích ứng ----
        QualityTarget Q(int size, int rate, int fps) => new(size, rate, fps);
        var steady = new QualityGovernor(Q(480, 600_000, 15), Q(240, 150_000, 5), Q(480, 600_000, 15));
        var calm = new QualitySignal(15, 5, 20, true, 1);
        var noChange = Enumerable.Range(0, 6).Select(i => steady.Step(calm, i * 10_000)).All(d => !d.Changed);
        Check(noChange, "a healthy stream is left alone");

        var slow = new QualityGovernor(Q(480, 600_000, 15), Q(240, 150_000, 5), Q(480, 600_000, 15));
        var hotSignal = new QualitySignal(15, 60, 40, true, 1);     // 60 ms giải mã: quá 60% ngân sách 66 ms
        Check(!slow.Step(hotSignal, 0).Changed, "one slow second is not enough to change");
        var first = slow.Step(hotSignal, 1000);
        Check(first.Changed && first.Target.BitRate == 420_000 && first.Target.MaxFps == 15,
            "two slow seconds lower the bitrate first", $"{first.Target.BitRate} {first.Target.MaxFps}fps");
        Check(!slow.Step(hotSignal, 2000).Changed, "the cooldown blocks an immediate second change");

        var lowFps = new QualityGovernor(Q(480, 600_000, 15), Q(240, 150_000, 5), Q(480, 600_000, 15));
        lowFps.Step(new QualitySignal(4, 5, 20, true, 1), 0);
        Check(lowFps.Step(new QualitySignal(4, 5, 20, true, 1), 1000).Changed, "frames arriving too slowly is stress too");

        var cpu = new QualityGovernor(Q(480, 600_000, 15), Q(240, 150_000, 5), Q(480, 600_000, 15));
        cpu.Step(new QualitySignal(15, 5, 95, true, 1), 0);
        Check(cpu.Step(new QualitySignal(15, 5, 95, true, 1), 1000).Changed, "a loaded CPU lowers quality as well");

        // bitrate đã ở sàn → hạ fps; fps ở sàn → hạ độ phân giải; không bao giờ dưới sàn
        var atFloorRate = new QualityGovernor(Q(480, 150_000, 15), Q(240, 150_000, 5), Q(480, 150_000, 15));
        atFloorRate.Step(hotSignal, 0);
        var fpsStep = atFloorRate.Step(hotSignal, 1000);
        Check(fpsStep.Target.MaxFps < 15 && fpsStep.Target.BitRate == 150_000, "at the bitrate floor the fps drops", $"{fpsStep.Target.MaxFps}fps");

        var atFloorFps = new QualityGovernor(Q(480, 150_000, 5), Q(240, 150_000, 5), Q(480, 150_000, 5));
        var slowAt5 = new QualitySignal(1, 5, 20, true, 1);   // 1 fps so với mức 5 fps = quá tải
        atFloorFps.Step(slowAt5, 0);
        var sizeStep = atFloorFps.Step(slowAt5, 1000);
        Check(sizeStep.Target.MaxSize < 480 && sizeStep.Target.MaxSize % 2 == 0 && sizeStep.Target.MaxSize >= 240,
            "at the fps floor the resolution drops, staying even", $"{sizeStep.Target.MaxSize}p");

        var floorGov = new QualityGovernor(Q(480, 600_000, 15), Q(240, 150_000, 5), Q(480, 600_000, 15));
        var below = false;
        for (int i = 0; i < 60; i++)
        {
            var d = floorGov.Step(hotSignal, i * 5000L);
            if (d.Target.MaxSize < 240 || d.Target.BitRate < 150_000 || d.Target.MaxFps < 5) below = true;
        }
        Check(!below, "the governor never goes below its floor", floorGov.Current.ToString());

        var rise = new QualityGovernor(Q(480, 420_000, 15), Q(240, 150_000, 5), Q(480, 600_000, 15));
        QualityDecision? up = null;
        for (int i = 0; i < 6 && up is not { Changed: true }; i++) up = rise.Step(calm, i * 10_000);
        Check(up is { Changed: true } && up.Target.BitRate > 420_000 && up.Target.BitRate <= 600_000,
            "a healthy stream gets its bitrate back", $"{up?.Target.BitRate}");

        var ceilingGov = new QualityGovernor(Q(480, 600_000, 15), Q(240, 150_000, 5), Q(480, 600_000, 15));
        var above = false;
        for (int i = 0; i < 40; i++)
        {
            var d = ceilingGov.Step(calm, i * 10_000L);
            if (d.Target.MaxSize > 480 || d.Target.BitRate > 600_000 || d.Target.MaxFps > 15) above = true;
        }
        Check(!above, "the governor never goes above its ceiling");

        var stalledGov = new QualityGovernor(Q(480, 600_000, 15), Q(240, 150_000, 5), Q(480, 600_000, 15));
        var stalledSignal = new QualitySignal(0, 90, 95, true, 1, AgeMs: 6000);
        var stalled1 = stalledGov.Step(stalledSignal, 0);
        var stalled2 = stalledGov.Step(stalledSignal, 1000);
        Check(!stalled1.Changed && !stalled2.Changed && stalledGov.Current == Q(480, 600_000, 15),
            "a stalled stream is not treated as overload", stalledGov.LastReason);

        // ---- 3) mức đã học được dùng khi mở luồng, không đè lên lựa chọn thủ công ----
        var settings = new Settings { ThumbMaxSize = 480, ThumbBitRate = 600_000, ThumbFps = 15 };
        var learned = new DeviceMeta { AutoMaxSize = 360, AutoBitRate = 300_000, AutoFps = 10 };
        var learnedPlan = DeviceVideo.Thumb(learned, settings);
        Check(learnedPlan == new VideoPlan(360, 300_000, 10), "the learned level is used on the next start", learnedPlan.ToString());
        var manual = new DeviceMeta { ThumbMaxSize = 720, ThumbFps = 30, ThumbBitRate = 2_000_000, AutoMaxSize = 360, AutoFps = 10, AutoBitRate = 300_000 };
        Check(DeviceVideo.Thumb(manual, settings) == new VideoPlan(720, 2_000_000, 30), "a manual choice always wins", DeviceVideo.Thumb(manual, settings).ToString());
        Check(DeviceVideo.ThumbBase(learned, settings) == new VideoPlan(480, 600_000, 15), "the base plan ignores what was learned");
        Check(AutoQuality.FloorOf(new VideoPlan(480, 600_000, 15)) == new QualityTarget(240, 150_000, 5), "the floor is a quarter of the base");

        // ---- 3b) ngân sách giải mã chia theo số máy đang mở ----
        Check(LoadBudget.SharePerStream(1, 12) == 540_000_000, "one stream on 12 cores gets the whole budget",
            LoadBudget.SharePerStream(1, 12).ToString());
        Check(LoadBudget.SharePerStream(300, 12) == 1_800_000, "300 streams share the same budget", LoadBudget.SharePerStream(300, 12).ToString());
        Check(LoadBudget.PixelsPerSecond(480, 15) == 1_944_000, "cost is measured in pixels per second", LoadBudget.PixelsPerSecond(480, 15).ToString());
        var plenty = new VideoPlan(480, 600_000, 15);
        Check(LoadBudget.Fit(plenty, 540_000_000) == plenty, "a big budget cuts nothing");
        var crowded = LoadBudget.Fit(plenty, 1_800_000);
        Check(crowded.MaxSize == 480 && crowded.MaxFps == 12, "300 phones: 15 fps becomes 12, not a slideshow", crowded.ToString());
        var starved = LoadBudget.Fit(plenty, 500_000);
        Check(starved.MaxSize == 384 && starved.MaxFps == 5, "a tiny share cuts first fps then size", starved.ToString());
        var clamped = LoadBudget.Fit(plenty, 1);
        Check(clamped.MaxSize == 240 && clamped.MaxFps == 5, "the budget never goes below 240p/5 fps", clamped.ToString());
        Check(crowded.BitRate == 480_000, "the bitrate follows the pixels cut", crowded.BitRate.ToString());
        Check(starved.BitRate == 128_000, "a smaller picture also needs fewer bits", starved.BitRate.ToString());
        Check(clamped.BitRate == 120_000, "the bitrate has a floor of 120 kbps", clamped.BitRate.ToString());
        LoadBudget.SetActive(300, 12);
        var shared = LoadBudget.Apply(new VideoPlan(720, 2_000_000, 30));
        Check(shared.MaxSize == 480 && shared.MaxFps == 11 && shared.BitRate == 326_000,
            "opening a stream obeys the current share, bits included", shared.ToString());
        LoadBudget.SetActive(0, 12);
        Check(LoadBudget.Apply(new VideoPlan(720, 2_000_000, 30)) == new VideoPlan(720, 2_000_000, 30), "no streams means no cap");

        // ---- 4) chẩn đoán từ nhật ký ----
        var key = Diagnostics.Analyse(["adb: unauthorized", "device unauthorized"]);
        Check(key is { Code: "unauthorized" } && key.Hits == 2, "an unaccepted ADB key is recognised", key?.Code ?? "null");
        var samsung = Diagnostics.Analyse(["stack corruption detected", "ACodec reconfig"]);
        Check(samsung is { Code: "cmdline-stack", AutoFixable: true }, "the Samsung command-line crash is recognised", samsung?.Code ?? "null");
        var offline = Diagnostics.Analyse(["ERROR device offline"]);
        Check(offline is { Code: "offline" }, "an offline device is recognised regardless of case", offline?.Code ?? "null");
        var latest = Diagnostics.Analyse(["unauthorized", "reconnecting", "failed to connect"]);
        Check(latest is { Code: "wifi" }, "the most recent failure wins", latest?.Code ?? "null");
        Check(Diagnostics.Analyse([]) == null, "an empty log produces no diagnosis");

        // ---- 4b) nhật ký khởi động: thấy được ứng dụng chết ở mốc nào ----
        var line = StartupTrace.Format(new DateTime(2026, 1, 2, 3, 4, 5, 678), 1234, "shown", "mode all");
        Check(line == "2026-01-02 03:04:05.678 +1234 ms · shown · mode all", "a startup mark is timestamped and staged", line);
        Check(StartupTrace.Format(DateTime.MinValue, 0, "begin") == "0001-01-01 00:00:00.000 +0 ms · begin",
            "a mark without extra text has no trailing separator");
        StartupTrace.Mark("selfcheck", "stage probe");
        var tail = StartupTrace.Tail(5);
        Check(tail.Length > 0 && tail[^1].EndsWith("· selfcheck · stage probe", StringComparison.Ordinal),
            "the last stage can be read back for a crash report", tail.Length > 0 ? tail[^1] : "empty");
        Check(tail[^1].Contains(" ms · ", StringComparison.Ordinal), "each line carries the elapsed time");

        // ---- 4c) chẩn đoán khởi động tại chỗ (--doctor) ----
        var facts = Core.Doctor.Collect();
        var report = Core.Doctor.Report(facts);
        Check(report.Length > 60 && report.Contains(facts.DataDir, StringComparison.Ordinal) && report.Contains(facts.Cores.ToString(), StringComparison.Ordinal),
            "the doctor report carries the real machine facts", report.Split('\n').FirstOrDefault() ?? "");
        var marks = StartupTrace.Tail(12);
        Check(marks.Length > 0 && report.Contains(marks[^1], StringComparison.Ordinal), "the doctor report includes the latest startup marks");
        Check(Core.Doctor.LooksUnhealthy(new DoctorFacts(0, 0, false, false, "", "", "", null, "")),
            "an unwritable data folder counts as a problem", "no write access");
        Check(facts.Cores > 0 && facts.DataWritable, "this machine can write its data folder", facts.DataWritable ? "yes" : "no");
        var doctorFile = Path.Combine(Path.GetTempPath(), "cp-doctor-" + Environment.ProcessId + ".txt");
        Check(Core.Doctor.Write(doctorFile) == doctorFile && File.Exists(doctorFile) && new FileInfo(doctorFile).Length > 100,
            "the doctor saves its report to a file");
        try { File.Delete(doctorFile); } catch { }

        // ---- 4d) bằng chứng từ chính Windows (nhật ký sự kiện, Defender, dump) ----
        const string logSample = "Event[0]:\r\n  Date: 2026-11-27T18:02:11.000\r\n  Event ID: 1000\r\n" +
            "Faulting application name: ControlPhone.exe\r\nFaulting module name: igd10iumd64.dll\r\n" +
            "Exception code: 0xc0000005\r\n\r\n" +
            "Event[1]:\r\n  Date: 2026-11-27T18:05:00.000\r\nFaulting application name: notepad.exe\r\n" +
            "Faulting module name: uxtheme.dll\r\n\r\n";
        var faults = CrashForensics.ParseEventLog(logSample);
        Check(faults.Count == 1, "another program's crash is not reported as ours", faults.Count.ToString());
        Check(faults[0].Module == "igd10iumd64.dll" && faults[0].Code == "0xc0000005",
            "the faulting module and exception code are kept", faults[0].Line);
        Check(faults[0].When.StartsWith("2026-11-27T18:02", StringComparison.Ordinal), "the crash time is kept", faults[0].When);
        Check(CrashForensics.ParseEventLog("") .Count == 0 && CrashForensics.ParseEventLog(null).Count == 0,
            "empty evidence is not a fault");
        Check(CrashForensics.ParseDefender("2026-11-27 | C:\\Temp\\adb.exe | 1\n2026-11-27 | C:\\x\\other.zip | 2").Count == 1,
            "a quarantined adb.exe counts, an unrelated file does not");
        Check(CrashForensics.Relevant("C:\\Temp\\scrcpy-server") && !CrashForensics.Relevant("C:\\x\\other.zip"),
            "the app's own extracted tools are relevant, other files are not");
        var forensicsReport = CrashForensics.Report((faults, ["C:\\x\\ControlPhone.exe | 1"], ["ControlPhone.exe.1.dmp · 9 KB"]));
        Check(forensicsReport.Contains("igd10iumd64.dll", StringComparison.Ordinal) && forensicsReport.Contains("dmp", StringComparison.Ordinal),
            "the forensics report shows module, dump and detection", forensicsReport.Split('\n').Length.ToString());
        var collected = CrashForensics.Collect();
        Check(collected.Faults != null && collected.Detections != null && collected.Dumps != null,
            "collecting Windows evidence never throws", $"{collected.Faults.Count} lỗi");

        // ---- 5) công cụ AI mới có mặt và có mô tả ----
        var host = new FakeHost();
        var tools = new Ai.AiToolHost(host);
        var names = tools.Specs.Select(x => x.Name).ToList();
        Check(names.Contains("get_stream_stats") && names.Contains("diagnose_device") && names.Contains("set_stream_quality"),
            "the performance tools are registered", string.Join(",", names.Where(n => n.StartsWith("get_stream") || n.StartsWith("diagnose") || n.StartsWith("set_stream"))));
        var statsCall = tools.ExecuteAsync("get_stream_stats", "{}", CancellationToken.None).GetAwaiter().GetResult();
        Check(statsCall.Ok && statsCall.Content.Contains("no video yet"), "stats are reported per phone", statsCall.Summary);
        var noValues = tools.ExecuteAsync("set_stream_quality", "{\"device\":\"1\"}", CancellationToken.None).GetAwaiter().GetResult();
        Check(!noValues.Ok, "a quality change with no values is refused");
        var qualityCall = tools.ExecuteAsync("set_stream_quality", "{\"device\":\"1\",\"fps\":8,\"kbps\":250}", CancellationToken.None).GetAwaiter().GetResult();
        Check(qualityCall.Ok && qualityCall.Content.Contains("8fps"), "a quality change reaches the host", qualityCall.Content);
        var misTarget = tools.ExecuteAsync("diagnose_device", "{\"device\":\"Phone\"}", CancellationToken.None).GetAwaiter().GetResult();
        Check(!misTarget.Ok, "an ambiguous phone name is refused, not guessed", misTarget.Summary);
        var stalledTools = new Ai.AiToolHost(new FakeHost { AddStalled = true });
        var stalledCall = stalledTools.ExecuteAsync("get_stream_stats", "{\"device\":\"3\"}", CancellationToken.None).GetAwaiter().GetResult();
        Check(stalledCall.Ok && stalledCall.Content.Contains("stalled") && stalledCall.Content.Contains("12 skipped"),
            "a stalled phone is reported as stalled, with its skips", stalledCall.Content);
    }

    // ---------------- cửa sổ hội thoại gửi cho nhà cung cấp AI ----------------
    static void HistoryChecks()
    {
        static Ai.AiMessage U(string t) => new() { Role = "user", Content = t };
        static Ai.AiMessage A(string t) => new() { Role = "assistant", Content = t };
        static Ai.AiMessage AC(string t, params string[] ids) =>
            new() { Role = "assistant", Content = t, ToolCalls = ids.Select(id => new Ai.AiToolCall(id, "shell", "{}")).ToList() };
        static Ai.AiMessage R(string id) => new() { Role = "tool", Content = "ok", ToolCallId = id, ToolName = "shell" };
        static string Shape(IEnumerable<Ai.AiMessage> ms) => string.Join(",", ms.Select(m => m.Role));

        // 1) cửa sổ cắt ngang cặp gọi/kết quả: phải bắt đầu ở lượt người dùng
        var cut = new List<Ai.AiMessage> { AC("", "c1"), R("c1"), A("xong"), U("máy nào đang bật?") };
        var w1 = Ai.AiAgent.Window(cut);
        Check(w1[0].Role == "user" && w1.Count == 1, "history starts at a user turn", Shape(w1));

        // 2) hội thoại dài: giữ tối đa 60 lượt và vẫn bắt đầu ở lượt người dùng
        var longChat = new List<Ai.AiMessage>();
        for (int i = 0; i < 40; i++) { longChat.Add(U($"câu {i}")); longChat.Add(AC("", $"c{i}")); longChat.Add(R($"c{i}")); }
        var w2 = Ai.AiAgent.Window(longChat);
        Check(w2.Count <= 60 && w2[0].Role == "user" && w2[^1].Role == "tool", "long history is capped and aligned", $"{w2.Count} {Shape(w2.Take(2))}");

        // 3) lời gọi không có kết quả (người dùng bấm dừng giữa vòng) không được gửi đi
        var half = new List<Ai.AiMessage> { U("cài giúp tôi"), AC("", "a", "b"), R("a") };
        var w3 = Ai.AiAgent.Window(half);
        var calls3 = w3.Where(m => m.ToolCalls != null).SelectMany(m => m.ToolCalls!).Select(c => c.Id).ToList();
        Check(calls3.SequenceEqual(["a"]), "unanswered tool calls are dropped", string.Join(",", calls3));

        // 4) assistant chỉ có lời gọi mà không có kết quả nào → bỏ hẳn
        var orphanCall = new List<Ai.AiMessage> { U("chạy lệnh"), AC("", "z"), U("thôi") };
        var w4 = Ai.AiAgent.Window(orphanCall);
        Check(w4.All(m => m.ToolCalls == null), "an assistant with no answered call is dropped", Shape(w4));

        // 5) kết quả công cụ mồ côi (assistant đã bị cắt) → bỏ
        var orphanResult = new List<Ai.AiMessage> { R("ghost"), U("xin chào") };
        var w5 = Ai.AiAgent.Window(orphanResult);
        Check(w5.Count == 1 && w5[0].Role == "user", "orphan tool results are dropped", Shape(w5));

        // 6) câu trả lời lỗi và assistant rỗng không gửi lại
        var noise = new List<Ai.AiMessage> { U("hỏi"), new Ai.AiMessage { Role = "assistant", Content = "lỗi mạng", Error = true }, A(""), U("hỏi lại") };
        var w6 = Ai.AiAgent.Window(noise);
        Check(w6.All(m => !m.Error) && w6.All(m => !(m.Role == "assistant" && m.Content == "")), "error and empty assistant turns are dropped", Shape(w6));

        // 7) hội thoại bình thường giữ nguyên thứ tự và mã lời gọi
        var good = new List<Ai.AiMessage> { U("kiểm tra pin"), AC("đang kiểm tra", "k1"), R("k1"), A("pin 80%") };
        var w7 = Ai.AiAgent.Window(good);
        Check(Shape(w7) == "user,assistant,tool,assistant" && w7[2].ToolCallId == "k1", "a valid exchange is kept as is", Shape(w7));

        // 8) lượt mới nhất của người dùng không bao giờ bị cắt
        var many = new List<Ai.AiMessage>();
        for (int i = 0; i < 80; i++) many.Add(U($"câu {i}"));
        Check(Ai.AiAgent.Window(many)[^1].Content == "câu 79", "the newest user turn always survives");
    }

    // ---------------- giao diện: bảng màu, thước đo, độ tương phản WCAG ----------------
    static void ThemeChecks()
    {
        var missing = Theme.Required.Where(k => !Theme.Palette.ContainsKey(k)).ToList();
        Check(missing.Count == 0, "every brush used by the UI is in the palette", string.Join(", ", missing));

        var broken = new List<string>();
        foreach (var (key, (dark, light)) in Theme.Palette)
            foreach (var hex in new[] { dark, light })
                try { Theme.Parse(hex); } catch { broken.Add($"{key}={hex}"); }
        Check(broken.Count == 0, "every colour parses", string.Join(", ", broken));

        var same = Theme.Palette.Where(kv => !Theme.ThemeInvariant.Contains(kv.Key) && kv.Value.Dark == kv.Value.Light)
            .Select(kv => kv.Key).ToList();
        Check(same.Count == 0, "light and dark really differ", string.Join(", ", same));

        // chữ trên nền: thân bài ≥ 7:1 (AAA), chữ phụ và màu trạng thái ≥ 4,5:1 (AA)
        var weak = new List<string>();
        foreach (var theme in new[] { "dark", "light" })
        {
            string C(string key) => theme == "dark" ? Theme.Palette[key].Dark : Theme.Palette[key].Light;
            void Need(string fg, string bg, double min) {
                var r = Theme.Contrast(C(fg), C(bg));
                if (r < min) weak.Add($"{theme} {fg}/{bg} {r:0.00}");
            }
            Need("Text", "Bg", 7); Need("Text", "Bg2", 7); Need("Text", "Hover", 7);
            Need("Muted", "Bg", 4.5); Need("Muted", "Bg2", 4.5);
            Need("Ok", "Bg", 4.5); Need("Warn", "Bg", 4.5); Need("Bad", "Bg", 4.5); Need("Accent", "Bg", 4.5);
            var (start, stop) = Theme.AccentStops(theme);
            if (Theme.Contrast("#FFFFFF", start) < 4.5) weak.Add($"{theme} white/grad-start");
            if (Theme.Contrast("#FFFFFF", stop) < 4.5) weak.Add($"{theme} white/grad-stop");
            if (Theme.Contrast("#FFFFFF", C("ToastOk")) < 4.5) weak.Add($"{theme} white/ToastOk");
            if (Theme.Contrast("#FFFFFF", C("ToastErr")) < 4.5) weak.Add($"{theme} white/ToastErr");
            if (Theme.Contrast("#FFFFFF", C("ToastInfo")) < 4.5) weak.Add($"{theme} white/ToastInfo");
        }
        Check(weak.Count == 0, "text keeps WCAG contrast in both themes", string.Join(" · ", weak));

        Check(Theme.RCtl < Theme.RCard && Theme.RCard < Theme.RPill, "corner radii are a scale",
            $"{Theme.RCtl} < {Theme.RCard} < {Theme.RPill}");
        Check(Theme.FsCap < Theme.FsBody && Theme.FsBody < Theme.FsH2 && Theme.FsH2 < Theme.FsH1, "font sizes are a scale",
            $"{Theme.FsCap} < {Theme.FsBody} < {Theme.FsH2} < {Theme.FsH1}");

        var soft = Theme.Parse(Theme.Palette["AccentSoft"].Dark);
        Check(soft.A is > 0 and < 255, "soft accent is translucent (tints any surface)", $"alpha {soft.A}");
        Check(Theme.Contrast("#000000", "#FFFFFF") is > 20.9 and < 21.1, "contrast helper matches WCAG extremes",
            Theme.Contrast("#000000", "#FFFFFF").ToString("0.00"));
    }

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
