namespace RestaurantManagement.Web.Models;

// Candidate: tài khoản khớp định danh (kể cả khi đăng nhập thất bại) để ghi nhật ký đúng tài khoản/vai trò.
public sealed record LoginResult(LoginUser? User, int RemainingSeconds = 0, LoginUser? Candidate = null);
