using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ControlPhone.Core;

namespace ControlPhone.Ai;

/// <summary>Tuỳ chọn của trợ lý AI (không chứa khoá API).</summary>
public sealed class AiSettings
{
    /// <summary>Số vòng gọi công cụ tối đa cho một yêu cầu (chặn vòng lặp vô hạn).</summary>
    public int MaxToolRounds { get; set; } = 8;
    /// <summary>Hỏi người dùng trước khi chạy công cụ nhạy cảm (cài APK, gỡ ứng dụng, shell, gửi văn bản…).</summary>
    public bool RequireApproval { get; set; } = true;
    /// <summary>Hiện chi tiết tham số / kết quả của từng công cụ trong khung chat.</summary>
    public bool ShowToolDetails { get; set; } = true;
    /// <summary>Hướng dẫn thêm của người dùng, ghép vào lời nhắc hệ thống.</summary>
    public string ExtraSystemPrompt { get; set; } = "";
    public bool ShowPlan { get; set; } = true;
}

public sealed class AiChatMeta
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string ProviderId { get; set; } = "";
    public string Model { get; set; } = "";
    public DateTime Updated { get; set; } = DateTime.Now;
    public int Messages { get; set; }
}

public sealed class AiChat
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string ProviderId { get; set; } = "";
    public string Model { get; set; } = "";
    public DateTime Updated { get; set; } = DateTime.Now;
    public List<AiMessage> Messages { get; set; } = [];
}

public sealed class AiState
{
    public List<AiProvider> Providers { get; set; } = [];
    public AiSettings Settings { get; set; } = new();
    public List<AiChatMeta> Chats { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>
/// Lưu cấu hình nhà cung cấp AI và lịch sử hội thoại trong data/ai/ (tách khỏi config.json để xuất/nhập cài đặt
/// không bao giờ mang theo khoá API). Khoá API chỉ được ghi ở dạng mã hoá DPAPI.
/// </summary>
public static class AiStore
{
    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };
    static readonly object Gate = new();
    static AiState? state;
    public static string? LoadError { get; private set; }

    static string Dir => Path.Combine(Store.DataDir, "ai");
    static string StateFile => Path.Combine(Dir, "ai.json");
    static string ChatFile(string id) => Path.Combine(Dir, "chats", id + ".json");

    public static AiState State
    {
        get { lock (Gate) return state ??= Load(); }
    }

    public static IReadOnlyList<AiProvider> Providers => State.Providers;
    public static AiSettings Settings => State.Settings;

    static AiState Load()
    {
        try
        {
            if (File.Exists(StateFile))
            {
                var st = JsonSerializer.Deserialize<AiState>(File.ReadAllText(StateFile), Json) ?? new();
                st.Providers ??= [];
                st.Settings ??= new();
                st.Chats ??= [];
                Core.SettingsSanitizer.Apply(st.Settings);
                return st;
            }
        }
        catch (Exception e) { LoadError = e.Message; }
        return new AiState();
    }

    public static void Save()
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var tmp = StateFile + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(State, Json));
                File.Move(tmp, StateFile, overwrite: true);
            }
            catch (Exception e) { LoadError = e.Message; }
        }
    }

    public static AiProvider? Get(string id) => Providers.FirstOrDefault(p => p.Id == id);

    public static AiProvider? Default => Providers.FirstOrDefault(p => p.IsDefault) ?? Providers.FirstOrDefault();

    /// <summary>Khoá API đã giải mã (chỉ trong bộ nhớ, không ghi ra đĩa).</summary>
    public static string KeyOf(AiProvider p) => AiSecret.Unprotect(p.KeyProtected);

    public static void SetKey(AiProvider p, string? plain)
    {
        p.KeyProtected = string.IsNullOrWhiteSpace(plain) ? "" : AiSecret.Protect(plain.Trim());
    }

    public static void Upsert(AiProvider p, bool makeDefault)
    {
        lock (Gate)
        {
            var i = State.Providers.FindIndex(x => x.Id == p.Id);
            if (i >= 0) State.Providers[i] = p; else State.Providers.Add(p);
            if (makeDefault || State.Providers.Count == 1) SetDefault(p.Id);
        }
        Save();
    }

    /// <summary>Cộng dồn thống kê sử dụng của một nhà cung cấp (số liệu do nhà cung cấp trả về trong phản hồi).</summary>
    public static void AddUsage(string providerId, AiUsage usage)
    {
        lock (Gate)
        {
            var p = State.Providers.FirstOrDefault(x => x.Id == providerId);
            if (p == null || !usage.Any) return;
            p.Requests++;
            p.TokensIn += usage.InputTokens;
            p.TokensOut += usage.OutputTokens;
            p.LastUsed = DateTime.Now;
        }
        Save();
    }

    public static void SetDefault(string id)
    {
        foreach (var p in Providers) p.IsDefault = p.Id == id;
        Save();
    }

    public static void Delete(string id)
    {
        lock (Gate)
        {
            State.Providers.RemoveAll(p => p.Id == id);
            if (State.Providers.Count > 0 && !State.Providers.Any(p => p.IsDefault)) State.Providers[0].IsDefault = true;
        }
        Save();
    }

    /// <summary>Xoá toàn bộ dữ liệu AI: nhà cung cấp, khoá, hội thoại (yêu cầu của phần quyền riêng tư).</summary>
    public static void DeleteAll()
    {
        lock (Gate)
        {
            state = new AiState();
            try { if (Directory.Exists(Path.Combine(Dir, "chats"))) Directory.Delete(Path.Combine(Dir, "chats"), true); } catch { }
            try { if (File.Exists(StateFile)) File.Delete(StateFile); } catch { }
            try { if (File.Exists(StateFile + ".bak")) File.Delete(StateFile + ".bak"); } catch { }
        }
    }

    // ---------------- hội thoại ----------------
    public static AiChat NewChat(AiProvider? p, string title = "")
    {
        var chat = new AiChat
        {
            Id = DateTime.Now.ToString("yyyyMMdd-HHmmss-") + Guid.NewGuid().ToString("n")[..6],
            Title = title,
            ProviderId = p?.Id ?? "",
            Model = p?.Model ?? "",
        };
        lock (Gate)
        {
            State.Chats.Insert(0, new AiChatMeta { Id = chat.Id, Title = chat.Title, ProviderId = chat.ProviderId, Model = chat.Model });
            while (State.Chats.Count > 200) State.Chats.RemoveAt(State.Chats.Count - 1); // giữ tối đa 200 hội thoại
        }
        SaveChat(chat);
        Save();
        return chat;
    }

    public static AiChat? LoadChat(string id)
    {
        try
        {
            var f = ChatFile(id);
            return File.Exists(f) ? JsonSerializer.Deserialize<AiChat>(File.ReadAllText(f), Json) : null;
        }
        catch { return null; }
    }

    public static void SaveChat(AiChat chat)
    {
        try
        {
            chat.Updated = DateTime.Now;
            Directory.CreateDirectory(Path.Combine(Dir, "chats"));
            var f = ChatFile(chat.Id);
            var tmp = f + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(chat, Json));
            File.Move(tmp, f, overwrite: true);
            lock (Gate)
            {
                var meta = State.Chats.FirstOrDefault(c => c.Id == chat.Id);
                if (meta == null) { meta = new AiChatMeta { Id = chat.Id }; State.Chats.Insert(0, meta); }
                meta.Title = chat.Title;
                meta.ProviderId = chat.ProviderId;
                meta.Model = chat.Model;
                meta.Updated = chat.Updated;
                meta.Messages = chat.Messages.Count(m => m.Role is "user" or "assistant");
            }
        }
        catch (Exception e) { LoadError = e.Message; }
    }

    public static void DeleteChat(string id)
    {
        lock (Gate) State.Chats.RemoveAll(c => c.Id == id);
        try { File.Delete(ChatFile(id)); } catch { }
        Save();
    }
}
