using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ControlPhone.Ai;

namespace ControlPhone.Core;

/// <summary>
/// Bản sao lưu toàn bộ dữ liệu người dùng (tên máy, số thứ tự, nhóm, cài đặt, tuỳ chọn giao diện, lịch sử hội thoại AI)
/// thành một tệp ZIP để chuyển máy hoặc khôi phục sau khi cài lại. Khoá API KHÔNG bao giờ nằm trong bản sao lưu.
/// </summary>
public static class UserBackup
{
    public const string App = "ControlPhone";
    public const int Version = 1;
    const string ManifestEntry = "backup.json";
    const string DataEntry = "data.json";
    const string ChatPrefix = "chats/";

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Nội dung một bản sao lưu: phần dữ liệu chung + từng hội thoại AI (đúng định dạng tệp của AiStore).</summary>
    public sealed class Doc
    {
        public Settings? Settings { get; set; }
        public string? Theme { get; set; }
        public string? Lang { get; set; }
        public double? TileW { get; set; }
        public string? Sort { get; set; }

        /// <summary>Thứ tự ô tự xếp (3.13+); bản sao lưu cũ không có trường này.</summary>
        public List<string>? Order { get; set; }
        public bool? HideOffline { get; set; }
        public AiSettings? Ai { get; set; }
        public Dictionary<string, DeviceMeta> Devices { get; set; } = [];
        public List<string> Groups { get; set; } = [];
        public int NextNum { get; set; } = 1;
    }

    public sealed class Manifest
    {
        public string App { get; set; } = UserBackup.App;
        public int Version { get; set; } = UserBackup.Version;
        public DateTime At { get; set; } = DateTime.Now;
        public int Devices { get; set; }
        public int Groups { get; set; }
        public int Chats { get; set; }
        /// <summary>Luôn false: khoá API được mã hoá DPAPI cho từng máy và không rời khỏi máy.</summary>
        public bool ApiKeys { get; set; }
    }

    public sealed record Info(Manifest Manifest, Doc Doc, List<string> ChatIds)
    {
        public bool Ok => Manifest.App == App;
    }

    // ---------------- dựng / đọc (thuần, tự kiểm tra được) ----------------
    public static byte[] Build(Doc doc, IEnumerable<(string Id, string Json)> chats)
    {
        var list = chats.Where(c => !string.IsNullOrWhiteSpace(c.Id) && !string.IsNullOrWhiteSpace(c.Json)).ToList();
        var manifest = new Manifest
        {
            Devices = doc.Devices.Count,
            Groups = doc.Groups.Count,
            Chats = list.Count,
            ApiKeys = false,
        };
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            Write(zip, ManifestEntry, JsonSerializer.Serialize(manifest, Json));
            Write(zip, DataEntry, JsonSerializer.Serialize(doc, Json));
            foreach (var (id, json) in list) Write(zip, ChatPrefix + SafeId(id) + ".json", json);
        }
        return ms.ToArray();
    }

    public static Info Read(byte[] zipBytes)
    {
        using var ms = new MemoryStream(zipBytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var manifestEntry = zip.GetEntry(ManifestEntry) ?? throw new InvalidDataException("thiếu backup.json");
        var manifest = JsonSerializer.Deserialize<Manifest>(ReadText(manifestEntry), Json) ?? throw new InvalidDataException("backup.json rỗng");
        if (manifest.App != App) throw new InvalidDataException($"tệp không phải bản sao lưu {App}");
        if (manifest.Version > Version) throw new InvalidDataException($"bản sao lưu phiên bản {manifest.Version} mới hơn ứng dụng này ({Version})");
        var dataEntry = zip.GetEntry(DataEntry) ?? throw new InvalidDataException("thiếu data.json");
        var doc = JsonSerializer.Deserialize<Doc>(ReadText(dataEntry), Json) ?? throw new InvalidDataException("data.json rỗng");
        var chats = new List<string>();
        foreach (var e in zip.Entries.Where(e => e.FullName.StartsWith(ChatPrefix, StringComparison.Ordinal) && e.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            chats.Add(Path.GetFileNameWithoutExtension(e.Name));
        return new Info(manifest, doc, chats);
    }

    /// <summary>Nội dung các hội thoại trong bản sao lưu (id → JSON đúng định dạng tệp của AiStore).</summary>
    public static Dictionary<string, string> ReadChats(byte[] zipBytes)
    {
        var map = new Dictionary<string, string>();
        using var ms = new MemoryStream(zipBytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        foreach (var e in zip.Entries)
            if (e.FullName.StartsWith(ChatPrefix, StringComparison.Ordinal) && e.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                map[Path.GetFileNameWithoutExtension(e.Name)] = ReadText(e);
        return map;
    }

    public static string SafeId(string id)
    {
        var sb = new StringBuilder();
        foreach (var c in id ?? "")
            sb.Append(char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_');
        var s = sb.ToString().Trim('_');
        return s.Length == 0 ? "chat" : s.Length > 60 ? s[..60] : s;
    }

    // ---------------- áp vào dữ liệu hiện có (thuần) ----------------
    /// <summary>
    /// Gộp bản sao lưu vào trạng thái hiện tại: máy trùng số sê-ri thì lấy tên/nhóm từ bản sao lưu, máy khác giữ nguyên;
    /// nhóm được hợp nhất; số thứ tự kế tiếp chỉ tăng. Trả về số máy và số nhóm mới được thêm.
    /// </summary>
    public static (int Devices, int Groups) MergeInto(StoreState target, Doc doc)
    {
        var devices = 0;
        foreach (var (id, meta) in doc.Devices)
        {
            if (string.IsNullOrWhiteSpace(id) || meta == null) continue;
            if (!target.Devices.ContainsKey(id)) devices++;
            target.Devices[id] = meta;
        }
        var groups = 0;
        foreach (var g in doc.Groups)
        {
            if (string.IsNullOrWhiteSpace(g) || target.Groups.Contains(g)) continue;
            target.Groups.Add(g);
            groups++;
        }
        target.NextNum = Math.Max(target.NextNum, Math.Max(1, doc.NextNum));
        return (devices, groups);
    }

    /// <summary>Chỉ những hội thoại chưa có trên máy này — không ghi đè hội thoại người dùng đã tiếp tục sau bản sao lưu.</summary>
    public static List<string> MissingChats(IEnumerable<string> backupIds, Func<string, bool> exists) =>
        backupIds.Where(id => !exists(id)).ToList();

    // ---------------- đường dùng trong ứng dụng ----------------
    public static string DefaultName() => $"ControlPhone-backup-{DateTime.Now:yyyyMMdd-HHmm}.zip";

    public static byte[] BuildFromStore()
    {
        var doc = new Doc
        {
            Settings = Store.Settings,
            Theme = Store.Ui.Theme,
            Lang = Store.Ui.Lang,
            TileW = Store.Ui.TileW,
            Sort = Store.Ui.Sort,
            Order = [.. Store.Ui.Order],
            HideOffline = Store.Ui.HideOffline,
            Ai = AiStore.Settings,
            Devices = new Dictionary<string, DeviceMeta>(Store.State.Devices),
            Groups = [.. Store.State.Groups],
            NextNum = Store.State.NextNum,
        };
        var chats = new List<(string, string)>();
        var dir = Path.Combine(Store.DataDir, "ai", "chats");
        if (Directory.Exists(dir))
            foreach (var f in Directory.GetFiles(dir, "*.json"))
            {
                try { chats.Add((Path.GetFileNameWithoutExtension(f), File.ReadAllText(f, Encoding.UTF8))); }
                catch { }
            }
        return Build(doc, chats);
    }

    static void Write(ZipArchive zip, string name, string text)
    {
        var e = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = e.Open();
        var bytes = Encoding.UTF8.GetBytes(text);
        s.Write(bytes, 0, bytes.Length);
    }

    static string ReadText(ZipArchiveEntry e)
    {
        using var s = e.Open();
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    }
}
