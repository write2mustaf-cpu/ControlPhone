using System.Text;

namespace ControlPhone.Ai;

public enum AiEventKind { Status, AssistantText, ToolStart, ToolEnd, Error, Stopped, Usage }

public sealed record AiEvent(AiEventKind Kind, string Text, AiToolCall? Call = null, AiToolResult? Result = null, bool? Ok = null);

/// <summary>
/// Vòng chạy của trợ lý: gửi hội thoại + danh sách công cụ cho nhà cung cấp, chạy các công cụ mà AI yêu cầu
/// (có hỏi người dùng với công cụ nhạy cảm), gửi kết quả lại cho AI, lặp tới khi có câu trả lời cuối hoặc hết số vòng.
/// </summary>
public sealed class AiAgent(AiToolHost tools)
{
    /// <summary>Kết quả công cụ dài hơn mức này bị rút gọn trước khi gửi lại cho AI (tránh tràn ngữ cảnh và hoá đơn token).</summary>
    public const int MaxToolChars = 8000;

    public event Action<AiEvent>? Event;
    /// <summary>Hỏi người dùng trước khi chạy công cụ nhạy cảm. Trả về false = từ chối.</summary>
    public Func<AiToolSpec, string, Task<bool>>? Approve;
    /// <summary>Chạy trên luồng giao diện (tuỳ chọn) — dùng để cập nhật khung chat.</summary>
    public Action<Action>? UiPost;

    void Emit(AiEvent e)
    {
        if (UiPost != null) UiPost(() => Event?.Invoke(e));
        else Event?.Invoke(e);
    }

    public async Task RunAsync(AiProvider provider, string apiKey, AiChat chat, string userText, CancellationToken ct)
    {
        chat.Messages.Add(new AiMessage { Role = "user", Content = userText });
        if (string.IsNullOrWhiteSpace(chat.Title) || chat.Title == "…")
            chat.Title = userText.Length > 48 ? userText[..48] + "…" : userText;
        chat.ProviderId = provider.Id;
        chat.Model = provider.Model;

        var specs = tools.Specs;
        var settings = AiStore.Settings;
        var rounds = Math.Clamp(settings.MaxToolRounds, 1, 20);
        var usage = new AiUsage(0, 0);
        var clock = System.Diagnostics.Stopwatch.StartNew();

        for (int round = 0; round < rounds; round++)
        {
            ct.ThrowIfCancellationRequested();
            Emit(new AiEvent(AiEventKind.Status, round == 0 ? "thinking" : "continuing"));

            var history = BuildHistory(chat, provider);
            var reply = await AiClient.ChatAsync(provider, apiKey, history, specs, ct).ConfigureAwait(false);
            usage += reply.Usage;

            if (!reply.Ok)
            {
                var err = new AiMessage { Role = "assistant", Content = reply.Error ?? "error", Error = true };
                chat.Messages.Add(err);
                Emit(new AiEvent(AiEventKind.Error, err.Content));
                return;
            }

            var assistant = new AiMessage
            {
                Role = "assistant",
                Content = reply.Text,
                ToolCalls = reply.ToolCalls.Count > 0 ? reply.ToolCalls : null,
            };
            chat.Messages.Add(assistant);
            if (!string.IsNullOrEmpty(reply.Text)) Emit(new AiEvent(AiEventKind.AssistantText, reply.Text));

            if (reply.ToolCalls.Count == 0)
            {
                if (string.IsNullOrEmpty(reply.Text)) Emit(new AiEvent(AiEventKind.AssistantText, "(no answer)"));
                Finish(provider, usage, clock);
                return;
            }

            foreach (var call in reply.ToolCalls)
            {
                ct.ThrowIfCancellationRequested();
                var spec = specs.FirstOrDefault(s => s.Name == call.Name) ?? new AiToolSpec(call.Name, "", "{}");
                Emit(new AiEvent(AiEventKind.ToolStart, spec.Name, call));
                AiToolResult result;
                var needsApproval = spec.Sensitive && AiStore.Settings.RequireApproval;
                if (needsApproval && Approve != null)
                {
                    var summary = DescribeCall(call, spec);
                    bool ok;
                    try { ok = await Approve(spec, summary).ConfigureAwait(false); }
                    catch { ok = false; }
                    if (!ok)
                    {
                        result = new AiToolResult(false, "The user declined this operation. Do not retry it unless the user asks.", "declined by user");
                        Emit(new AiEvent(AiEventKind.ToolEnd, spec.Name, call, result, false));
                        chat.Messages.Add(ToolMessage(call, result));
                        continue;
                    }
                }
                result = await tools.ExecuteAsync(call.Name, call.ArgumentsJson, ct).ConfigureAwait(false);
                Emit(new AiEvent(AiEventKind.ToolEnd, spec.Name, call, result, result.Ok));
                chat.Messages.Add(ToolMessage(call, result));
            }
        }

        var note = new AiMessage { Role = "assistant", Content = "Stopped after the maximum number of tool rounds. Tell the user what was done and what is left.", Error = false };
        chat.Messages.Add(note);
        Emit(new AiEvent(AiEventKind.AssistantText, note.Content));
        Finish(provider, usage, clock);
    }

    /// <summary>Ghi thống kê sử dụng (nếu nhà cung cấp báo) và thông báo cho khung chat.</summary>
    void Finish(AiProvider provider, AiUsage usage, System.Diagnostics.Stopwatch clock)
    {
        clock.Stop();
        if (usage.Any) AiStore.AddUsage(provider.Id, usage);
        Emit(new AiEvent(AiEventKind.Usage, UsageText(usage, clock.Elapsed)));
    }

    public static string UsageText(AiUsage usage, TimeSpan took)
    {
        var secs = took.TotalSeconds < 10 ? took.TotalSeconds.ToString("0.0") : Math.Round(took.TotalSeconds).ToString("0");
        var t = $"{secs} s";
        if (!usage.Any) return t;
        return $"{usage.Total:N0} tokens ({usage.InputTokens:N0} in / {usage.OutputTokens:N0} out) · {t}";
    }

    /// <summary>
    /// Rút gọn kết quả công cụ dài (log, dump, danh sách lớn): giữ phần đầu, phần cuối và số ký tự đã bỏ,
    /// để mô hình vẫn thấy dữ liệu thật mà không làm tràn ngữ cảnh.
    /// </summary>
    public static string TruncateForModel(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= MaxToolChars) return text ?? "";
        const int head = 6000, tail = 1500;
        var skipped = text.Length - head - tail;
        return text[..head] + $"\n\n[... {skipped:N0} characters omitted to fit the model context ...]\n\n" + text[^tail..];
    }

    static AiMessage ToolMessage(AiToolCall call, AiToolResult result) => new()
    {
        Role = "tool",
        ToolCallId = call.Id,
        ToolName = call.Name,
        Content = TruncateForModel(result.Content),
        ToolOk = result.Ok,
        ToolSummary = result.Summary,
    };

    /// <summary>Lịch sử gửi cho nhà cung cấp: bỏ câu lỗi, gộp lại cho gọn, cắt bớt nếu quá dài.</summary>
    static List<AiMessage> BuildHistory(AiChat chat, AiProvider provider)
    {
        var list = new List<AiMessage>();
        var usable = chat.Messages.Where(m => !m.Error && !(m.Role == "assistant" && string.IsNullOrEmpty(m.Content) && m.ToolCalls == null)).ToList();
        // giữ tối đa ~60 lượt gần nhất (tiết kiệm token, vẫn đủ ngữ cảnh)
        if (usable.Count > 60) usable = usable.Skip(usable.Count - 60).ToList();
        foreach (var m in usable)
            list.Add(new AiMessage
            {
                Role = m.Role,
                Content = m.Content,
                ToolCalls = m.ToolCalls,
                ToolCallId = m.ToolCallId,
                ToolName = m.ToolName,
            });
        return list;
    }

    /// <summary>Mô tả ngắn để người dùng duyệt trước khi chạy công cụ nhạy cảm.</summary>
    public static string DescribeCall(AiToolCall call, AiToolSpec spec)
    {
        var sb = new StringBuilder();
        sb.Append(call.Name);
        try
        {
            var o = System.Text.Json.Nodes.JsonNode.Parse(string.IsNullOrWhiteSpace(call.ArgumentsJson) ? "{}" : call.ArgumentsJson) as System.Text.Json.Nodes.JsonObject;
            if (o != null)
            {
                var parts = new List<string>();
                foreach (var (k, v) in o)
                {
                    var s = v?.ToString() ?? "";
                    if (s.Length > 120) s = s[..120] + "…";
                    parts.Add($"{k}: {s}");
                }
                if (parts.Count > 0) sb.Append(" — ").Append(string.Join(", ", parts));
            }
        }
        catch { }
        return sb.ToString();
    }
}
