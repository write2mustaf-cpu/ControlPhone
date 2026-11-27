using System.Text.Json.Serialization;

namespace ControlPhone.Ai;

/// <summary>Giao thức của nhà cung cấp AI: 3 API gốc + mọi dịch vụ tương thích OpenAI (OpenRouter, DeepSeek, Groq, Ollama…).</summary>
public enum AiProviderKind { OpenAi, Gemini, Anthropic, OpenAiCompatible }

/// <summary>Một nhà cung cấp AI đã cấu hình. Khoá API chỉ nằm ở dạng mã hoá DPAPI (<see cref="KeyProtected"/>).</summary>
public sealed class AiProvider
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public string Name { get; set; } = "";
    public AiProviderKind Kind { get; set; } = AiProviderKind.OpenAi;
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "";
    /// <summary>Khoá API sau khi mã hoá bằng DPAPI (base64). Không bao giờ ghi khoá thô ra đĩa hay nhật ký.</summary>
    public string KeyProtected { get; set; } = "";
    public int MaxTokens { get; set; } = 1200;
    public double Temperature { get; set; } = 0.4;
    public int TimeoutSeconds { get; set; } = 120;
    public bool IsDefault { get; set; }
    public DateTime? LastOk { get; set; }
    public string LastError { get; set; } = "";
    /// <summary>Thống kê sử dụng luỹ kế (chỉ số do nhà cung cấp trả về; 0 nếu nhà cung cấp không báo).</summary>
    public long Requests { get; set; }
    public long TokensIn { get; set; }
    public long TokensOut { get; set; }
    public DateTime? LastUsed { get; set; }

    public long TokensTotal => TokensIn + TokensOut;

    public bool HasKey => !string.IsNullOrEmpty(KeyProtected);
    public string Display => string.IsNullOrEmpty(Name) ? Model : Name;

    public AiProvider Clone() => (AiProvider)MemberwiseClone();
}

public sealed record AiToolCall(string Id, string Name, string ArgumentsJson);

public sealed class AiMessage
{
    public string Role { get; set; } = "user"; // system | user | assistant | tool
    public string Content { get; set; } = "";
    public List<AiToolCall>? ToolCalls { get; set; }
    public string? ToolCallId { get; set; }
    public string? ToolName { get; set; }
    /// <summary>Câu trả lời bị lỗi (mạng, khoá sai…) — hiện khác màu trong khung chat.</summary>
    public bool Error { get; set; }
    public DateTime At { get; set; } = DateTime.Now;
    /// <summary>Chỉ để hiện lại thẻ công cụ đã chạy trong khung chat (không gửi lại cho nhà cung cấp).</summary>
    public string? ToolResult { get; set; }
    public bool? ToolOk { get; set; }
    public string? ToolSummary { get; set; }

    [JsonIgnore] public bool IsTool => Role == "tool";
}

public sealed record AiUsage(int InputTokens, int OutputTokens)
{
    public int Total => InputTokens + OutputTokens;
    public static AiUsage operator +(AiUsage a, AiUsage b) => new(a.InputTokens + b.InputTokens, a.OutputTokens + b.OutputTokens);
    public bool Any => Total > 0;
}

/// <summary>Mô tả công cụ gửi cho AI (JSON Schema cho tham số) + cờ nhạy cảm cần người dùng đồng ý.</summary>
public sealed record AiToolSpec(string Name, string Description, string ParametersJson, bool Sensitive = false);

/// <summary>Kết quả một lượt gọi nhà cung cấp.</summary>
public sealed class AiReply
{
    public string Text { get; init; } = "";
    public List<AiToolCall> ToolCalls { get; init; } = [];
    public AiUsage Usage { get; init; } = new(0, 0);
    public string? Error { get; init; }
    public bool Ok => Error == null;
}

/// <summary>Thông tin máy rút gọn đưa vào lời nhắc hệ thống / công cụ.</summary>
public sealed record AiDeviceInfo(
    string Id, int Num, string Name, string Model, string Android, string Status, string Transport,
    int? Battery, bool? Charging, bool Root, string Ip, IReadOnlyList<string> Groups, bool Paused, bool Streaming, string? Error);

/// <summary>Mẫu cấu hình sẵn cho từng nhà cung cấp (người dùng vẫn tự nhập khoá).</summary>
public static class AiPresets
{
    public sealed record Preset(AiProviderKind Kind, string Name, string BaseUrl, string Model, string KeyHint, string ModelHint);

    public static readonly Preset[] All =
    [
        new(AiProviderKind.Gemini, "Google Gemini", "https://generativelanguage.googleapis.com", "gemini-2.5-flash",
            "aistudio.google.com → Get API key", "gemini-2.5-flash · gemini-2.5-pro · gemini-2.0-flash"),
        new(AiProviderKind.OpenAi, "OpenAI", "https://api.openai.com/v1", "gpt-4o-mini",
            "platform.openai.com → API keys", "gpt-4o-mini · gpt-4o · gpt-4.1"),
        new(AiProviderKind.Anthropic, "Anthropic Claude", "https://api.anthropic.com", "claude-sonnet-4-5",
            "console.anthropic.com → API keys", "claude-sonnet-4-5 · claude-opus-4-1 · claude-haiku-4-5"),
        new(AiProviderKind.OpenAiCompatible, "OpenRouter", "https://openrouter.ai/api/v1", "anthropic/claude-sonnet-4.5",
            "openrouter.ai → Keys", "nhà cung cấp/model (vd. openai/gpt-4o-mini)"),
        new(AiProviderKind.OpenAiCompatible, "DeepSeek", "https://api.deepseek.com/v1", "deepseek-chat",
            "platform.deepseek.com → API keys", "deepseek-chat · deepseek-reasoner"),
        new(AiProviderKind.OpenAiCompatible, "Groq", "https://api.groq.com/openai/v1", "llama-3.3-70b-versatile",
            "console.groq.com → API keys", "llama-3.3-70b-versatile · mixtral-8x7b-32768"),
        new(AiProviderKind.OpenAiCompatible, "Ollama (máy cục bộ)", "http://localhost:11434/v1", "qwen2.5:7b",
            "không cần khoá (để trống)", "qwen2.5:7b · llama3.1:8b · bất kỳ model đã pull"),
        new(AiProviderKind.OpenAiCompatible, "Tuỳ chỉnh (tương thích OpenAI)", "", "",
            "khoá API của dịch vụ", "model của dịch vụ"),
    ];

    public static string KindLabel(AiProviderKind k) => k switch
    {
        AiProviderKind.Gemini => "Google Gemini",
        AiProviderKind.OpenAi => "OpenAI",
        AiProviderKind.Anthropic => "Anthropic Claude",
        _ => "OpenAI-compatible",
    };
}
