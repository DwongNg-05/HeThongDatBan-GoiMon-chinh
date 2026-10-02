using System.Security.Cryptography;
using System.Text;

namespace RestaurantManagement.Web.Services.EmailVerification;

/// <summary>
/// Mã xác minh: 6 ký tự gồm chữ in hoa và số, không có ký tự đặc biệt.
/// Bỏ các ký tự dễ nhầm (0/O, 1/I/L) để ai cũng đọc và gõ lại đúng.
/// </summary>
public static class VerificationCode
{
    public const int Length = 6;
    public const string Letters = "ABCDEFGHJKMNPQRSTUVWXYZ";
    public const string Digits = "23456789";
    public const string Alphabet = Letters + Digits;

    public static string Generate()
    {
        while (true)
        {
            var chars = new char[Length];
            for (var i = 0; i < Length; i++) chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
            var code = new string(chars);
            // Luôn có cả chữ lẫn số.
            if (code.Any(c => Letters.Contains(c)) && code.Any(c => Digits.Contains(c))) return code;
        }
    }

    /// <summary>Chuẩn hoá mã người dùng gõ: bỏ khoảng trắng và gạch nối, đổi sang chữ in hoa.</summary>
    public static string Normalize(string? input) =>
        new string((input ?? "").Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant();

    public static bool IsWellFormed(string code) =>
        code.Length == Length && code.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9');

    /// <summary>SHA-256 gắn với tài khoản và phiên đăng nhập; database chỉ lưu giá trị băm.</summary>
    public static byte[] Hash(string code, int userId, Guid sessionId) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}:{sessionId:N}:{code}"));

    /// <summary>Che bớt email để hiển thị: nguyenvana@gmail.com → n*******a@gmail.com.</summary>
    public static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0) return email;
        var name = email[..at];
        var masked = name.Length <= 2 ? name[0] + new string('*', Math.Max(1, name.Length - 1))
            : name[0] + new string('*', name.Length - 2) + name[^1];
        return masked + email[at..];
    }
}
