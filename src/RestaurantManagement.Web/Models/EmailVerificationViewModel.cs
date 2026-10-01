namespace RestaurantManagement.Web.Models;

public sealed class EmailVerificationViewModel
{
    /// <summary>Tài khoản đã có email đã xác minh.</summary>
    public bool HasSavedEmail { get; init; }
    /// <summary>Email (đã che bớt) đang nhận mã; null nếu chưa nhập email.</summary>
    public string? MaskedEmail { get; init; }
    public bool CodeSent { get; init; }
    public int ResendAfterSeconds { get; init; }
    public int ValidMinutes { get; init; }
    public string? ReturnUrl { get; init; }
    public string? Code { get; set; }
    public string? Error { get; set; }
    public string? Info { get; set; }
}
