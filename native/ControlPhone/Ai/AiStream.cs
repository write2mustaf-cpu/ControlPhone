using System.Text;
using System.Text.Json.Nodes;

namespace ControlPhone.Ai;

/// <summary>Một sự kiện text/event-stream đã tách xong (tên sự kiện có thể rỗng với OpenAI).</summary>
public sealed record SseEvent(string Name, string Data);

/// <summary>
/// Tách luồng SSE thành sự kiện theo dòng, chịu được gói tin bị cắt giữa dòng và cả CRLF. Hàm thuần: bản tự kiểm tra
/// nuôi nó bằng chuỗi mẫu của từng nhà cung cấp mà không cần mạng.
/// </summary>
public sealed class SseParser
{
    readonly StringBuilder pending = new();
    readonly StringBuilder data = new();
    string name = "";

    public List<SseEvent> Feed(string chunk)
    {
        var events = new List<SseEvent>();
        pending.Append(chunk);
        var text = pending.ToString();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;
            var line = text[start..i];
            start = i + 1;
            if (line.EndsWith('\r')) line = line[..^1];
            if (line.Length == 0) { Emit(events); continue; }
            if (line[0] == ':') continue; // chú thích / keep-alive
            var colon = line.IndexOf(':');
            var field = colon < 0 ? line : line[..colon];
            var value = colon < 0 ? "" : line[(colon + 1)..];
            if (value.StartsWith(' ')) value = value[1..];
            switch (field)
            {
                case "event": name = value; break;
                case "data":
                    if (data.Length > 0) data.Append('\n');
                    data.Append(value);
                    break;
            }
        }
        pending.Clear().Append(text[start..]);
        return events;
    }

    /// <summary>Kết thúc luồng: nhận nốt sự kiện cuối nếu nhà cung cấp không gửi dòng trống cuối cùng.</summary>
    public List<SseEvent> Flush()
    {
        var events = new List<SseEvent>();
        var rest = pending.ToString().TrimEnd('\r');
        pending.Clear();
        if (rest.StartsWith("data:", StringComparison.Ordinal))
        {
            var v = rest[5..];
            if (v.StartsWith(' ')) v = v[1..];
            if (data.Length > 0) data.Append('\n');
            data.Append(v);
        }
        Emit(events);
        return events;
    }

    void Emit(List<SseEvent> events)
    {
        if (data.Length == 0) { name = ""; return; }
        events.Add(new SseEvent(name, data.ToString()));
        data.Clear();
        name = "";
    }
}

/// <summary>
/// Ghép các mẩu SSE thành một câu trả lời hoàn chỉnh cho từng nhà cung cấp: chữ chạy dần, lời gọi công cụ bị cắt thành
/// nhiều mẩu (OpenAI theo index, Anthropic theo content block), và số token dùng được nhà cung cấp báo ở cuối.
/// </summary>
public sealed class StreamAccumulator
{
    sealed class CallBuilder
    {
        public string Id = "";
        public string Name = "";
        public readonly StringBuilder Args = new();
    }

    readonly AiProviderKind kind;
    readonly StringBuilder text = new();
    readonly List<AiToolCall> calls = [];
    readonly Dictionary<int, CallBuilder> streamingCalls = [];

    public StreamAccumulator(AiProviderKind kind) => this.kind = kind;

    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }
    public bool Empty => text.Length == 0 && calls.Count == 0 && streamingCalls.Count == 0;

    /// <summary>Đã nhận được chữ nào chưa (khác <see cref="Empty"/>: có thể chỉ có công cụ dở).</summary>
    public bool HasText => text.Length > 0;

    public string Text => text.ToString();

    public AiReply Result()
    {
        FlushCalls();
        return new AiReply
        {
            Text = text.ToString().Trim(),
            ToolCalls = calls,
            Usage = new AiUsage(InputTokens, OutputTokens),
        };
    }

    /// <summary>
    /// Luồng vỡ giữa chừng (quá thời gian, mạng rơi): giữ phần chữ đã nhận thay vì vứt đi rồi gửi lại cả câu — gửi lại vừa
    /// tốn tiền của người dùng vừa làm chữ đang hiện trên màn hình biến mất. Lời gọi công cụ dở dang bị bỏ: harg JSON của
    /// nó có thể bị cắt, chạy một công cụ với tham số nửa vời còn tệ hơn không chạy.
    /// </summary>
    public AiReply? Partial() => HasText
        ? new AiReply { Text = text.ToString().Trim(), Incomplete = true }
        : null;

    /// <summary>Nhận một sự kiện; trả về phần chữ mới để hiện dần (có thể rỗng).</summary>
    public string Feed(SseEvent ev)
    {
        JsonObject? o;
        try { o = JsonNode.Parse(ev.Data) as JsonObject; } catch { return ""; }
        if (o == null) return "";
        return kind switch
        {
            AiProviderKind.Gemini => FeedGemini(o),
            AiProviderKind.Anthropic => FeedAnthropic(ev.Name, o),
            _ => FeedOpenAi(o),
        };
    }

    static int Int(JsonNode? n)
    {
        try { return n is JsonValue v ? v.GetValue<int>() : 0; } catch { return 0; }
    }

    string FeedOpenAi(JsonObject o)
    {
        if (o["usage"] is JsonObject u)
        {
            InputTokens = Int(u["prompt_tokens"]) is var p && p > 0 ? p : InputTokens;
            OutputTokens = Int(u["completion_tokens"]) is var c && c > 0 ? c : OutputTokens;
        }
        var delta = (o["choices"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault()?["delta"] as JsonObject;
        var sb = new StringBuilder();
        switch (delta?["content"])
        {
            case JsonValue v: Append(v.ToString(), sb); break;
            case JsonArray a:
                foreach (var part in a.OfType<JsonObject>()) Append(part["text"]?.ToString() ?? "", sb);
                break;
        }
        if (delta?["tool_calls"] is JsonArray tcs)
            foreach (var tc in tcs.OfType<JsonObject>())
            {
                var idx = Int(tc["index"]);
                if (!streamingCalls.TryGetValue(idx, out var b)) streamingCalls[idx] = b = new CallBuilder();
                var id = tc["id"]?.ToString();
                if (!string.IsNullOrEmpty(id)) b.Id = id;
                var name = tc["function"]?["name"]?.ToString();
                if (!string.IsNullOrEmpty(name)) b.Name = name;
                var args = tc["function"]?["arguments"]?.ToString();
                if (!string.IsNullOrEmpty(args)) b.Args.Append(args);
            }
        return sb.ToString();
    }

    string FeedAnthropic(string eventName, JsonObject o)
    {
        switch (o["type"]?.ToString() ?? eventName)
        {
            case "message_start":
                InputTokens = Int(o["message"]?["usage"]?["input_tokens"]) is var it && it > 0 ? it : InputTokens;
                break;
            case "content_block_start":
            {
                var block = o["content_block"] as JsonObject;
                if (block?["type"]?.ToString() == "tool_use")
                    streamingCalls[Int(o["index"])] = new CallBuilder
                    {
                        Id = block["id"]?.ToString() ?? "",
                        Name = block["name"]?.ToString() ?? "",
                    };
                break;
            }
            case "content_block_delta":
            {
                var d = o["delta"] as JsonObject;
                switch (d?["type"]?.ToString())
                {
                    case "text_delta":
                    {
                        var sb = new StringBuilder();
                        Append(d["text"]?.ToString() ?? "", sb);
                        return sb.ToString();
                    }
                    case "input_json_delta":
                        if (streamingCalls.TryGetValue(Int(o["index"]), out var b)) b.Args.Append(d["partial_json"]?.ToString() ?? "");
                        break;
                }
                break;
            }
            case "message_delta":
                OutputTokens = Int(o["usage"]?["output_tokens"]) is var ot && ot > 0 ? ot : OutputTokens;
                break;
        }
        return "";
    }

    string FeedGemini(JsonObject o)
    {
        var sb = new StringBuilder();
        var parts = (o["candidates"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault()?["content"]?["parts"] as JsonArray;
        foreach (var part in parts?.OfType<JsonObject>() ?? [])
        {
            Append(part["text"]?.ToString() ?? "", sb);
            if (part["functionCall"] is JsonObject fc)
                calls.Add(new AiToolCall("call_" + Guid.NewGuid().ToString("n")[..8], fc["name"]?.ToString() ?? "", fc["args"]?.ToJsonString() ?? "{}"));
        }
        if (o["usageMetadata"] is JsonObject u)
        {
            InputTokens = Int(u["promptTokenCount"]) is var p && p > 0 ? p : InputTokens;
            OutputTokens = Int(u["candidatesTokenCount"]) is var c && c > 0 ? c : OutputTokens;
        }
        return sb.ToString();
    }

    void Append(string s, StringBuilder sb)
    {
        if (s.Length == 0) return;
        text.Append(s);
        sb.Append(s);
    }

    void FlushCalls()
    {
        foreach (var b in streamingCalls.OrderBy(k => k.Key).Select(k => k.Value))
            if (b.Name.Length > 0)
                calls.Add(new AiToolCall(
                    b.Id.Length > 0 ? b.Id : "call_" + Guid.NewGuid().ToString("n")[..8],
                    b.Name,
                    b.Args.Length > 0 ? b.Args.ToString() : "{}"));
        streamingCalls.Clear();
    }
}
