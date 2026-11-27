using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ControlPhone.Ai;

/// <summary>Yêu cầu HTTP đã dựng xong (tách riêng khỏi việc gửi để tự kiểm tra được khi không có mạng).</summary>
public sealed record AiHttpRequest(string Url, Dictionary<string, string> Headers, string Body);

/// <summary>
/// Gọi API của các nhà cung cấp AI: OpenAI (và mọi dịch vụ tương thích OpenAI), Google Gemini, Anthropic Claude.
/// Chuyển đổi hội thoại + danh sách công cụ sang đúng định dạng của từng API và đọc kết quả về một dạng chung.
/// </summary>
public static class AiClient
{
    static readonly HttpClient Http = new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    public const string AnthropicVersion = "2023-06-01";

    // ---------------- URL ----------------
    public static string ChatUrl(AiProvider p)
    {
        var b = (p.BaseUrl ?? "").Trim().TrimEnd('/');
        if (b == "") throw new InvalidOperationException("Chưa nhập địa chỉ API (Base URL)");
        switch (p.Kind)
        {
            case AiProviderKind.Gemini:
                if (b.EndsWith(":generateContent")) return b;
                if (b.EndsWith("/v1beta") || b.EndsWith("/v1")) return b + "/models/" + Uri.EscapeDataString(p.Model) + ":generateContent";
                return b + "/v1beta/models/" + Uri.EscapeDataString(p.Model) + ":generateContent";
            case AiProviderKind.Anthropic:
                if (b.EndsWith("/messages")) return b;
                return b + "/v1/messages";
            default:
                if (b.EndsWith("/chat/completions")) return b;
                return b + "/chat/completions";
        }
    }

    public static string ModelsUrl(AiProvider p)
    {
        var b = (p.BaseUrl ?? "").Trim().TrimEnd('/');
        if (b == "") throw new InvalidOperationException("Chưa nhập địa chỉ API (Base URL)");
        return p.Kind switch
        {
            AiProviderKind.Gemini => (b.EndsWith("/v1beta") || b.EndsWith("/v1") ? b : b + "/v1beta") + "/models",
            AiProviderKind.Anthropic => b + (b.EndsWith("/v1") ? "" : "/v1") + "/models",
            _ => b + "/models",
        };
    }

    // ---------------- dựng yêu cầu ----------------
    public static AiHttpRequest BuildChat(AiProvider p, string apiKey, IReadOnlyList<AiMessage> messages, IReadOnlyList<AiToolSpec> tools)
    {
        var headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" };
        JsonObject body = p.Kind switch
        {
            AiProviderKind.Gemini => BuildGemini(p, messages, tools),
            AiProviderKind.Anthropic => BuildAnthropic(p, messages, tools),
            _ => BuildOpenAi(p, messages, tools),
        };
        switch (p.Kind)
        {
            case AiProviderKind.Gemini:
                if (!string.IsNullOrEmpty(apiKey)) headers["x-goog-api-key"] = apiKey;
                break;
            case AiProviderKind.Anthropic:
                if (!string.IsNullOrEmpty(apiKey)) headers["x-api-key"] = apiKey;
                headers["anthropic-version"] = AnthropicVersion;
                break;
            default:
                if (!string.IsNullOrEmpty(apiKey)) headers["Authorization"] = "Bearer " + apiKey;
                if (p.Kind == AiProviderKind.OpenAiCompatible) { headers["HTTP-Referer"] = "https://github.com/write2mustaf-cpu/ControlPhone"; headers["X-Title"] = "ControlPhone"; }
                break;
        }
        return new AiHttpRequest(ChatUrl(p), headers, body.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
    }

    static JsonObject BuildOpenAi(AiProvider p, IReadOnlyList<AiMessage> messages, IReadOnlyList<AiToolSpec> tools)
    {
        var arr = new JsonArray();
        foreach (var m in messages)
        {
            var o = new JsonObject { ["role"] = m.Role };
            switch (m.Role)
            {
                case "assistant":
                    o["content"] = string.IsNullOrEmpty(m.Content) ? null : m.Content;
                    if (m.ToolCalls is { Count: > 0 })
                    {
                        var calls = new JsonArray();
                        foreach (var c in m.ToolCalls)
                            calls.Add(new JsonObject
                            {
                                ["id"] = c.Id,
                                ["type"] = "function",
                                ["function"] = new JsonObject { ["name"] = c.Name, ["arguments"] = string.IsNullOrWhiteSpace(c.ArgumentsJson) ? "{}" : c.ArgumentsJson },
                            });
                        o["tool_calls"] = calls;
                    }
                    break;
                case "tool":
                    o["role"] = "tool";
                    o["tool_call_id"] = m.ToolCallId ?? "";
                    o["content"] = m.Content;
                    break;
                default:
                    o["content"] = m.Content;
                    break;
            }
            arr.Add(o);
        }
        var body = new JsonObject
        {
            ["model"] = p.Model,
            ["messages"] = arr,
            ["max_tokens"] = Math.Clamp(p.MaxTokens, 64, 32000),
            ["temperature"] = Math.Clamp(p.Temperature, 0, 2),
        };
        if (tools.Count > 0)
        {
            var ta = new JsonArray();
            foreach (var t in tools)
                ta.Add(new JsonObject
                {
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = t.Name,
                        ["description"] = t.Description,
                        ["parameters"] = ParseSchema(t.ParametersJson),
                    },
                });
            body["tools"] = ta;
            body["tool_choice"] = "auto";
        }
        return body;
    }

    static JsonObject BuildGemini(AiProvider p, IReadOnlyList<AiMessage> messages, IReadOnlyList<AiToolSpec> tools)
    {
        var system = string.Join("\n\n", messages.Where(m => m.Role == "system" && m.Content.Length > 0).Select(m => m.Content));
        var contents = new JsonArray();
        foreach (var m in messages.Where(m => m.Role != "system"))
        {
            var role = m.Role == "assistant" ? "model" : "user";
            var parts = new JsonArray();
            if (m.Role == "tool")
            {
                var resp = new JsonObject { ["result"] = m.Content };
                parts.Add(new JsonObject
                {
                    ["functionResponse"] = new JsonObject
                    {
                        ["name"] = string.IsNullOrEmpty(m.ToolName) ? "tool" : m.ToolName,
                        ["response"] = resp,
                    },
                });
            }
            else
            {
                if (!string.IsNullOrEmpty(m.Content)) parts.Add(new JsonObject { ["text"] = m.Content });
                foreach (var c in m.ToolCalls ?? [])
                    parts.Add(new JsonObject
                    {
                        ["functionCall"] = new JsonObject { ["name"] = c.Name, ["args"] = ParseArgs(c.ArgumentsJson) },
                    });
            }
            if (parts.Count == 0) continue;
            // Gemini yêu cầu vai trò luân phiên: gộp các lượt liền nhau cùng vai trò
            if (contents.Count > 0 && (string?)contents[^1]!["role"] == role)
            {
                var prev = contents[^1]!["parts"]!.AsArray();
                foreach (var part in parts.ToList()) { parts.Remove(part); prev.Add(part); }
            }
            else contents.Add(new JsonObject { ["role"] = role, ["parts"] = parts });
        }
        var body = new JsonObject
        {
            ["contents"] = contents,
            ["generationConfig"] = new JsonObject
            {
                ["maxOutputTokens"] = Math.Clamp(p.MaxTokens, 64, 32000),
                ["temperature"] = Math.Clamp(p.Temperature, 0, 2),
            },
        };
        if (system != "") body["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = system }) };
        if (tools.Count > 0)
        {
            var decls = new JsonArray();
            foreach (var t in tools)
                decls.Add(new JsonObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["parameters"] = ParseSchema(t.ParametersJson),
                });
            body["tools"] = new JsonArray(new JsonObject { ["functionDeclarations"] = decls });
        }
        return body;
    }

    static JsonObject BuildAnthropic(AiProvider p, IReadOnlyList<AiMessage> messages, IReadOnlyList<AiToolSpec> tools)
    {
        var system = string.Join("\n\n", messages.Where(m => m.Role == "system" && m.Content.Length > 0).Select(m => m.Content));
        var arr = new JsonArray();
        foreach (var m in messages.Where(m => m.Role != "system"))
        {
            var role = m.Role == "assistant" ? "assistant" : "user";
            var blocks = new JsonArray();
            if (m.Role == "tool")
            {
                blocks.Add(new JsonObject
                {
                    ["type"] = "tool_result",
                    ["tool_use_id"] = m.ToolCallId ?? "",
                    ["content"] = m.Content,
                });
            }
            else
            {
                if (!string.IsNullOrEmpty(m.Content)) blocks.Add(new JsonObject { ["type"] = "text", ["text"] = m.Content });
                foreach (var c in m.ToolCalls ?? [])
                    blocks.Add(new JsonObject
                    {
                        ["type"] = "tool_use",
                        ["id"] = c.Id,
                        ["name"] = c.Name,
                        ["input"] = ParseArgs(c.ArgumentsJson),
                    });
            }
            if (blocks.Count == 0) continue;
            if (arr.Count > 0 && (string?)arr[^1]!["role"] == role)
            {
                var prev = arr[^1]!["content"]!.AsArray();
                foreach (var b in blocks.ToList()) { blocks.Remove(b); prev.Add(b); }
            }
            else arr.Add(new JsonObject { ["role"] = role, ["content"] = blocks });
        }
        var body = new JsonObject
        {
            ["model"] = p.Model,
            ["max_tokens"] = Math.Clamp(p.MaxTokens, 64, 32000),
            ["temperature"] = Math.Clamp(p.Temperature, 0, 1),
            ["messages"] = arr,
        };
        if (system != "") body["system"] = system;
        if (tools.Count > 0)
        {
            var ta = new JsonArray();
            foreach (var t in tools)
                ta.Add(new JsonObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["input_schema"] = ParseSchema(t.ParametersJson),
                });
            body["tools"] = ta;
        }
        return body;
    }

    static JsonObject ParseSchema(string json)
    {
        try { return JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{\"type\":\"object\",\"properties\":{}}" : json)!.AsObject(); }
        catch { return new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }; }
    }

    static JsonObject ParseArgs(string json)
    {
        try { return JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json)!.AsObject(); }
        catch { return new JsonObject(); }
    }

    // ---------------- gửi / nhận ----------------
    public static async Task<AiReply> ChatAsync(AiProvider p, string apiKey, IReadOnlyList<AiMessage> messages,
        IReadOnlyList<AiToolSpec> tools, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(p.Model)) return new AiReply { Error = "Chưa chọn model cho nhà cung cấp này." };
        AiHttpRequest req;
        try { req = BuildChat(p, apiKey, messages, tools); }
        catch (Exception e) { return new AiReply { Error = AiSecret.Redact(e.Message, apiKey) }; }

        var (status, text, err) = await SendAsync(req, p.TimeoutSeconds, ct).ConfigureAwait(false);
        if (err != null) return new AiReply { Error = AiSecret.Redact(err, apiKey) };
        if (status < 200 || status >= 300)
            return new AiReply { Error = AiSecret.Redact($"HTTP {status}: {ExtractError(text)}", apiKey) };
        try { return ParseChat(p, text); }
        catch (Exception e) { return new AiReply { Error = "Không đọc được câu trả lời: " + AiSecret.Redact(e.Message, apiKey) }; }
    }

    // ---------------- phát trực tiếp (streaming) ----------------
    /// <summary>URL cho luồng SSE: Gemini đổi :generateContent thành :streamGenerateContent?alt=sse.</summary>
    public static string StreamUrl(AiProvider p)
    {
        var url = ChatUrl(p);
        if (p.Kind != AiProviderKind.Gemini) return url;
        return url.EndsWith(":generateContent")
            ? url[..^":generateContent".Length] + ":streamGenerateContent?alt=sse"
            : url;
    }

    /// <summary>Bật cờ stream trong phần thân yêu cầu (Gemini dùng tham số URL nên thân giữ nguyên).</summary>
    public static string StreamBody(AiProvider p, string body)
    {
        if (p.Kind == AiProviderKind.Gemini) return body;
        var o = JsonNode.Parse(body) as JsonObject ?? new JsonObject();
        o["stream"] = true;
        if (p.Kind == AiProviderKind.OpenAi) o["stream_options"] = new JsonObject { ["include_usage"] = true };
        return o.ToJsonString();
    }

    /// <summary>
    /// Gọi nhà cung cấp ở chế độ phát trực tiếp. Trả về null khi không dùng được (lỗi HTTP, luồng rỗng, mạng) để nơi gọi
    /// quay lại <see cref="ChatAsync"/> — chức năng đang chạy không bao giờ bị streaming làm hỏng.
    /// </summary>
    public static async Task<AiReply?> ChatStreamAsync(AiProvider p, string apiKey, IReadOnlyList<AiMessage> messages,
        IReadOnlyList<AiToolSpec> tools, Action<string> onDelta, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(p.Model)) return null;
        AiHttpRequest req;
        try { req = BuildChat(p, apiKey, messages, tools); } catch { return null; }

        var acc = new StreamAccumulator(p.Kind);
        var parser = new SseParser();
        var timeout = TimeSpan.FromSeconds(Math.Clamp(p.TimeoutSeconds <= 0 ? 120 : p.TimeoutSeconds, 10, 900));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            using var msg = new HttpRequestMessage(HttpMethod.Post, StreamUrl(p))
            {
                Content = new StringContent(StreamBody(p, req.Body), Encoding.UTF8, "application/json"),
            };
            foreach (var (k, v) in req.Headers) msg.Headers.TryAddWithoutValidation(k, v);
            using var res = await Http.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return null;
            await using var stream = await res.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (await reader.ReadLineAsync(cts.Token).ConfigureAwait(false) is { } line)
                foreach (var ev in parser.Feed(line + "\n"))
                {
                    var delta = acc.Feed(ev);
                    if (delta.Length > 0) onDelta(delta);
                }
            foreach (var ev in parser.Flush())
            {
                var delta = acc.Feed(ev);
                if (delta.Length > 0) onDelta(delta);
            }
            return acc.Empty ? null : acc.Result();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    static async Task<(int Status, string Body, string? Error)> SendAsync(AiHttpRequest req, int timeoutSeconds, CancellationToken ct)
    {
        var timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds <= 0 ? 120 : timeoutSeconds, 10, 900));
        for (int attempt = 0; ; attempt++)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            try
            {
                using var msg = new HttpRequestMessage(HttpMethod.Post, req.Url) { Content = new StringContent(req.Body, Encoding.UTF8, "application/json") };
                foreach (var (k, v) in req.Headers) msg.Headers.TryAddWithoutValidation(k, v);
                using var res = await Http.SendAsync(msg, HttpCompletionOption.ResponseContentRead, cts.Token).ConfigureAwait(false);
                var body = await res.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                int code = (int)res.StatusCode;
                // 429 / 5xx: thử lại 2 lần (nhà cung cấp hay báo quá tải tạm thời)
                if ((code == 429 || code >= 500) && attempt < 2)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(1200 * (attempt + 1)), ct).ConfigureAwait(false);
                    continue;
                }
                return (code, body, null);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                if (attempt < 1) continue;
                return (0, "", $"Hết thời gian chờ ({timeout.TotalSeconds:0} giây) — kiểm tra mạng hoặc tăng thời gian chờ.");
            }
            catch (OperationCanceledException) { return (0, "", "Đã dừng."); }
            catch (HttpRequestException e)
            {
                if (attempt < 1) { await Task.Delay(600, CancellationToken.None).ConfigureAwait(false); continue; }
                return (0, "", "Lỗi mạng: " + e.Message);
            }
            catch (Exception e) { return (0, "", e.Message); }
        }
    }

    static string ExtractError(string body)
    {
        try
        {
            var o = JsonNode.Parse(body) as JsonObject;
            if (o?["error"] is JsonObject e)
            {
                var m = e["message"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(m)) return m;
            }
            if (o?["error"] is JsonValue v) return v.ToString();
            if (o?["message"] is JsonValue mv) return mv.ToString();
        }
        catch { }
        var s = body.Replace("\r", " ").Replace("\n", " ").Trim();
        return s.Length > 400 ? s[..400] : s;
    }

    // ---------------- đọc kết quả ----------------
    public static AiReply ParseChat(AiProvider p, string json)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new JsonException("JSON rỗng");
        return p.Kind switch
        {
            AiProviderKind.Gemini => ParseGemini(root),
            AiProviderKind.Anthropic => ParseAnthropic(root),
            _ => ParseOpenAi(root),
        };
    }

    static AiReply ParseOpenAi(JsonObject root)
    {
        var choice = root["choices"]?.AsArray().FirstOrDefault() as JsonObject;
        var msg = choice?["message"] as JsonObject;
        var text = msg?["content"] switch
        {
            JsonValue v => v.ToString(),
            JsonArray a => string.Join("", a.Select(x => x?["text"]?.ToString() ?? "")),
            _ => "",
        };
        var calls = new List<AiToolCall>();
        if (msg?["tool_calls"] is JsonArray tcs)
            foreach (var tc in tcs.OfType<JsonObject>())
            {
                var fn = tc["function"] as JsonObject;
                if (fn == null) continue;
                calls.Add(new AiToolCall(
                    tc["id"]?.ToString() ?? "call_" + Guid.NewGuid().ToString("n")[..8],
                    fn["name"]?.ToString() ?? "",
                    fn["arguments"]?.ToString() ?? "{}"));
            }
        var usage = root["usage"] as JsonObject;
        return new AiReply
        {
            Text = text?.Trim() ?? "",
            ToolCalls = calls,
            Usage = new AiUsage(usage?["prompt_tokens"]?.GetValue<int>() ?? 0, usage?["completion_tokens"]?.GetValue<int>() ?? 0),
        };
    }

    static AiReply ParseGemini(JsonObject root)
    {
        var cand = root["candidates"]?.AsArray().FirstOrDefault() as JsonObject;
        var parts = cand?["content"]?["parts"] as JsonArray;
        var sb = new StringBuilder();
        var calls = new List<AiToolCall>();
        foreach (var part in parts?.OfType<JsonObject>() ?? [])
        {
            if (part["text"] is JsonValue t) sb.Append(t.ToString());
            if (part["functionCall"] is JsonObject fc)
                calls.Add(new AiToolCall("call_" + Guid.NewGuid().ToString("n")[..8], fc["name"]?.ToString() ?? "",
                    fc["args"]?.ToJsonString() ?? "{}"));
        }
        var blocked = root["promptFeedback"]?["blockReason"]?.ToString();
        var usage = root["usageMetadata"] as JsonObject;
        var reply = new AiReply
        {
            Text = sb.ToString().Trim(),
            ToolCalls = calls,
            Usage = new AiUsage(usage?["promptTokenCount"]?.GetValue<int>() ?? 0, usage?["candidatesTokenCount"]?.GetValue<int>() ?? 0),
        };
        if (reply.Text == "" && calls.Count == 0 && !string.IsNullOrEmpty(blocked))
            return new AiReply { Error = "Bị chặn bởi bộ lọc nội dung: " + blocked, Usage = reply.Usage };
        return reply;
    }

    static AiReply ParseAnthropic(JsonObject root)
    {
        var sb = new StringBuilder();
        var calls = new List<AiToolCall>();
        foreach (var block in (root["content"] as JsonArray)?.OfType<JsonObject>() ?? [])
        {
            switch (block["type"]?.ToString())
            {
                case "text": sb.Append(block["text"]?.ToString()); break;
                case "tool_use":
                    calls.Add(new AiToolCall(block["id"]?.ToString() ?? "call_" + Guid.NewGuid().ToString("n")[..8],
                        block["name"]?.ToString() ?? "", block["input"]?.ToJsonString() ?? "{}"));
                    break;
            }
        }
        var usage = root["usage"] as JsonObject;
        return new AiReply
        {
            Text = sb.ToString().Trim(),
            ToolCalls = calls,
            Usage = new AiUsage(usage?["input_tokens"]?.GetValue<int>() ?? 0, usage?["output_tokens"]?.GetValue<int>() ?? 0),
        };
    }

    // ---------------- kiểm tra kết nối / danh sách model ----------------
    public static async Task<(bool Ok, string Message)> TestAsync(AiProvider p, string apiKey, CancellationToken ct)
    {
        var probe = new List<AiMessage> { new() { Role = "user", Content = "ping — reply with OK" } };
        var small = p.Clone();
        small.MaxTokens = 64;
        small.TimeoutSeconds = Math.Min(p.TimeoutSeconds <= 0 ? 60 : p.TimeoutSeconds, 60);
        var r = await ChatAsync(small, apiKey, probe, [], ct).ConfigureAwait(false);
        if (!r.Ok) return (false, r.Error ?? "lỗi không rõ");
        var txt = string.IsNullOrWhiteSpace(r.Text) ? "(không có chữ trả về nhưng kết nối thành công)" : r.Text;
        return (true, txt.Length > 200 ? txt[..200] : txt);
    }

    public static async Task<(bool Ok, List<string> Models, string Message)> ListModelsAsync(AiProvider p, string apiKey, CancellationToken ct)
    {
        try
        {
            using var msg = new HttpRequestMessage(HttpMethod.Get, ModelsUrl(p));
            if (!string.IsNullOrEmpty(apiKey))
            {
                if (p.Kind == AiProviderKind.Gemini) msg.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);
                else if (p.Kind == AiProviderKind.Anthropic) { msg.Headers.TryAddWithoutValidation("x-api-key", apiKey); msg.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion); }
                else msg.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);
            }
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(25));
            using var res = await Http.SendAsync(msg, cts.Token).ConfigureAwait(false);
            var body = await res.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return (false, [], AiSecret.Redact($"HTTP {(int)res.StatusCode}: {ExtractError(body)}", apiKey));
            var root = JsonNode.Parse(body) as JsonObject;
            var list = new List<string>();
            var arr = root?["data"] as JsonArray ?? root?["models"] as JsonArray;
            foreach (var it in arr?.OfType<JsonObject>() ?? [])
            {
                var id = it["id"]?.ToString() ?? it["name"]?.ToString() ?? "";
                if (id.StartsWith("models/")) id = id[7..];
                if (id != "") list.Add(id);
            }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return (true, list, list.Count == 0 ? "Nhà cung cấp không trả về danh sách model — hãy nhập tay tên model." : $"{list.Count} model");
        }
        catch (Exception e) { return (false, [], AiSecret.Redact(e.Message, apiKey)); }
    }
}
