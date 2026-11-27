using System.Net.Http;
using System.Text.Json;

namespace ControlPhone.Core;

/// <summary>Thông tin một bản phát hành trên GitHub (chỉ phần ứng dụng dùng).</summary>
public sealed record ReleaseInfo(
    Version Version, string Tag, string Url, string Title, string Notes, DateTimeOffset? Published,
    long ZipBytes, string ZipName, long SetupBytes, string SetupName)
{
    public bool HasInstaller => SetupBytes > 0 && SetupName != "";

    /// <summary>Có gói ZIP chạy được để tải (không phải manifests/symbols).</summary>
    public bool HasZip => ZipBytes > 0 && ZipName != "";

    public string SetupUrl => $"{Updates.RepoUrl}/releases/download/{Tag}/{SetupName}";
    public string ZipUrl => $"{Updates.RepoUrl}/releases/download/{Tag}/{ZipName}";

    public string AssetsLine() => string.Join(" · ", new[]
    {
        HasInstaller ? $"Setup {Mb(SetupBytes)}" : null,
        ZipBytes > 0 ? $"ZIP {Mb(ZipBytes)}" : null,
    }.Where(x => x != null));

    static string Mb(long bytes) => (bytes / 1048576.0).ToString("0.0") + " MB";
}

/// <summary>Tệp đính kèm đã chọn của một bản phát hành — chỉ gói chạy được, không phải manifest hay ký hiệu gỡ lỗi.</summary>
public sealed record ReleaseAssets(string ZipName, long ZipBytes, string SetupName, long SetupBytes)
{
    public bool HasZip => ZipBytes > 0 && ZipName != "";
    public bool HasSetup => SetupBytes > 0 && SetupName != "";
}

/// <summary>
/// Kiểm tra bản phát hành mới trên GitHub. Chỉ đọc API công khai (không cần token), chỉ gửi User-Agent;
/// không gửi dữ liệu người dùng, không tự tải hay tự cài — người dùng bấm mới mở trang tải.
/// Hàm phân tích là hàm thuần để bản tự kiểm tra gọi trực tiếp (không cần mạng).
/// </summary>
public static class Updates
{
    public const string RepoUrl = "https://github.com/write2mustaf-cpu/ControlPhone";
    const string ApiUrl = "https://api.github.com/repos/write2mustaf-cpu/ControlPhone/releases/latest";

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

    public static Version Current => typeof(Updates).Assembly.GetName().Version ?? new Version(0, 0, 0);

    /// <summary>"v3.2.0" / "3.2" → Version; thẻ có tiền tố thử nghiệm ("-beta") bị bỏ qua.</summary>
    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var t = tag.Trim();
        if (t.StartsWith('v') || t.StartsWith('V')) t = t[1..];
        if (t.Contains('-') || t.Contains('+')) return null;
        var parts = t.Split('.');
        if (parts.Length is < 2 or > 3) return null;
        if (!parts.All(p => p.Length > 0 && p.All(char.IsAsciiDigit))) return null;
        return Version.TryParse(t, out var v) ? v : null;
    }

    public static bool IsNewer(Version candidate, Version current) => Normalize(candidate) > Normalize(current);

    static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    public static bool SameVersion(Version a, Version b) => Normalize(a) == Normalize(b);

    /// <summary>Tên tệp không phải bản chạy được: manifest gói cài, ký hiệu gỡ lỗi, mã nguồn, bảng băm.</summary>
    static readonly string[] NotRunnable = ["manifest", "symbol", "source", "checksum", "sha256", "debug"];

    /// <summary>
    /// Chọn đúng gói cài và gói ZIP trong danh sách đính kèm. Không dựa vào thứ tự API trả về: một bản phát hành có thêm
    /// tệp (manifest của winget/Scoop, ký hiệu gỡ lỗi) không được biến "Tải ZIP" thành một tệp YAML vài KB.
    /// Ưu tiên tên chuẩn (Setup… / …-win64.zip), sau đó tới tệp lớn nhất.
    /// </summary>
    public static ReleaseAssets PickAssets(IEnumerable<(string Name, long Size)> assets)
    {
        string zipName = "", setupName = "";
        long zipSize = 0, setupSize = 0;
        foreach (var (name, size) in assets)
        {
            if (string.IsNullOrWhiteSpace(name) || size <= 0) continue;
            var lower = name.ToLowerInvariant();
            if (lower.EndsWith(".exe"))
            {
                if (Better(lower, size, setupName, setupSize, setup: true)) { setupName = name; setupSize = size; }
            }
            else if (lower.EndsWith(".zip"))
            {
                if (NotRunnable.Any(lower.Contains)) continue;
                if (Better(lower, size, zipName, zipSize, setup: false)) { zipName = name; zipSize = size; }
            }
        }
        return new ReleaseAssets(zipName, zipSize, setupName, setupSize);
    }

    /// <summary>Điểm theo tên chuẩn; hoà điểm thì tệp lớn hơn thắng.</summary>
    static bool Better(string lower, long size, string currentName, long currentSize, bool setup)
    {
        if (currentName == "") return true;
        var current = currentName.ToLowerInvariant();
        int mine = Score(lower, setup), theirs = Score(current, setup);
        return mine > theirs || (mine == theirs && size > currentSize);
    }

    static int Score(string lower, bool setup) => setup
        ? (lower.Contains("setup") ? 100 : 0)
        : (lower.Contains("win64") ? 100 : 0) + (lower.Contains("portable") ? 50 : 0);

    /// <summary>Thông báo khi có bản mới hơn và người dùng chưa bấm «bỏ qua bản này».</summary>
    public static bool ShouldNotify(ReleaseInfo? info, string? skippedVersion) =>
        info != null && IsNewer(info.Version, Current) &&
        !(ParseTag(skippedVersion) is { } skipped && SameVersion(skipped, info.Version));

    /// <summary>Đọc JSON của API GitHub; trả về null cho bản nháp, bản thử nghiệm, thẻ không hợp lệ hoặc JSON hỏng.</summary>
    public static ReleaseInfo? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return null;
            if (Flag(r, "draft") || Flag(r, "prerelease")) return null;
            var tag = Str(r, "tag_name");
            if (ParseTag(tag) is not { } ver || string.IsNullOrEmpty(tag)) return null;
            var url = Str(r, "html_url") is { Length: > 0 } u ? u : RepoUrl + "/releases/latest";
            var title = Str(r, "name") is { Length: > 0 } n ? n : tag;
            var notes = Str(r, "body") ?? "";
            DateTimeOffset? published = DateTimeOffset.TryParse(Str(r, "published_at"), out var dt) ? dt : null;
            var listed = new List<(string Name, long Size)>();
            if (r.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                foreach (var a in assets.EnumerateArray())
                {
                    var name = Str(a, "name") ?? "";
                    var size = a.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var v) ? v : 0;
                    listed.Add((name, size));
                }
            var picked = PickAssets(listed);
            return new ReleaseInfo(ver, tag, url, title, notes, published, picked.ZipBytes, picked.ZipName, picked.SetupBytes, picked.SetupName);
        }
        catch { return null; }
    }

    static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static bool Flag(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>Gọi API; lỗi mạng/giới hạn tần suất trả về thông điệp thay vì ném lỗi.</summary>
    public static async Task<(ReleaseInfo? Info, string? Error)> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, ApiUrl);
            req.Headers.UserAgent.ParseAdd("ControlPhone/" + Current.ToString(3));
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(12));
            using var resp = await Http.SendAsync(req, cts.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return (null, $"HTTP {(int)resp.StatusCode}");
            var info = Parse(await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false));
            return info == null ? (null, "unreadable response") : (info, null);
        }
        catch (Exception e) { return (null, e.Message); }
    }
}
