using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace ControlPhone.Ai;

/// <summary>
/// Mã hoá khoá API bằng Windows DPAPI (CryptProtectData) — khoá chỉ giải mã được trên chính tài khoản Windows
/// đã lưu, nên tệp dữ liệu bị sao chép sang máy khác cũng không đọc được. Không dùng mật khẩu tự đặt và không
/// gửi khoá tới bất kỳ máy chủ nào ngoài nhà cung cấp AI mà người dùng chọn.
/// </summary>
public static class AiSecret
{
    const int CryptProtectUiForbidden = 0x1;
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ControlPhone.AI.Keys.v1");

    [StructLayout(LayoutKind.Sequential)]
    struct DataBlob { public int CbData; public IntPtr PbData; }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CryptProtectData(ref DataBlob pDataIn, string? szDataDescr, ref DataBlob pOptionalEntropy,
        IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DataBlob pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CryptUnprotectData(ref DataBlob pDataIn, IntPtr ppszDataDescr, ref DataBlob pOptionalEntropy,
        IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DataBlob pDataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr LocalFree(IntPtr hMem);

    /// <summary>DPAPI chỉ có trên Windows (ứng dụng chỉ chạy trên Windows).</summary>
    public static bool Available => OperatingSystem.IsWindows();

    public static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        return Convert.ToBase64String(Run(protect: true, Encoding.UTF8.GetBytes(plain)));
    }

    public static string Unprotect(string? protectedBase64)
    {
        if (string.IsNullOrEmpty(protectedBase64)) return "";
        try { return Encoding.UTF8.GetString(Run(protect: false, Convert.FromBase64String(protectedBase64))); }
        catch (Exception) { return ""; } // dữ liệu lạ / từ máy khác: coi như không có khoá
    }

    static byte[] Run(bool protect, byte[] input)
    {
        if (!Available) throw new PlatformNotSupportedException("DPAPI chỉ có trên Windows");
        var inHandle = GCHandle.Alloc(input, GCHandleType.Pinned);
        var entHandle = GCHandle.Alloc(Entropy, GCHandleType.Pinned);
        try
        {
            var inBlob = new DataBlob { CbData = input.Length, PbData = inHandle.AddrOfPinnedObject() };
            var entBlob = new DataBlob { CbData = Entropy.Length, PbData = entHandle.AddrOfPinnedObject() };
            DataBlob outBlob;
            bool ok = protect
                ? CryptProtectData(ref inBlob, "ControlPhone API key", ref entBlob, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out outBlob)
                : CryptUnprotectData(ref inBlob, IntPtr.Zero, ref entBlob, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out outBlob);
            if (!ok) throw new CryptographicException(Marshal.GetLastWin32Error());
            try
            {
                var res = new byte[outBlob.CbData];
                Marshal.Copy(outBlob.PbData, res, 0, outBlob.CbData);
                return res;
            }
            finally { LocalFree(outBlob.PbData); }
        }
        finally
        {
            inHandle.Free();
            entHandle.Free();
            Array.Clear(input);
        }
    }

    /// <summary>Che khoá trong mọi chuỗi sắp hiện ra / ghi nhật ký (lỗi mạng đôi khi chứa cả URL có khoá).</summary>
    public static string Redact(string? text, string? key)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var s = text;
        if (!string.IsNullOrEmpty(key) && key.Length >= 6) s = s.Replace(key, "••••");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"([?&](?:key|api_key|apikey|access_token|token)=)[^&\s]+", "$1••••", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"(Bearer\s+)[A-Za-z0-9._\-]{8,}", "$1••••");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"(sk-[A-Za-z0-9_\-]{6,})", "••••");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"(AIza[A-Za-z0-9_\-]{10,})", "••••");
        return s;
    }
}
