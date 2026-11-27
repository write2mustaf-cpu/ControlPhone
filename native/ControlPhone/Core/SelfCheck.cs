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
