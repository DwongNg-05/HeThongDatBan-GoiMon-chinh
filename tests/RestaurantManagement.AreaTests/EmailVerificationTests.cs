using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RestaurantManagement.Web.Models;
using RestaurantManagement.Web.Services.EmailVerification;

/// <summary>Xác minh đăng nhập bằng email: quy tắc mã, đối tượng phải xác minh, nội dung email (không cần database).</summary>
internal static class EmailVerificationTests
{
    internal static void Run(Action<bool, string> check)
    {
        var codes = Enumerable.Range(0, 2000).Select(_ => VerificationCode.Generate()).ToList();
        check(codes.All(c => Regex.IsMatch(c, "^[A-Z0-9]{6}$")), "Email code: 6 characters, uppercase letters and digits only");
        check(codes.All(c => c.Any(char.IsLetter) && c.Any(char.IsDigit)), "Email code: always contains both letters and digits");
        check(codes.All(c => !c.Any(ch => "0O1IL".Contains(ch))), "Email code: no easily confused characters (0, O, 1, I, L)");
        check(codes.Distinct().Count() > 1990, "Email code: random");
        check(VerificationCode.Normalize(" k7p-2qx ") == "K7P2QX", "Email code: typed lowercase, spaces and dashes are normalised");
        check(!VerificationCode.IsWellFormed("K7P2Q") && !VerificationCode.IsWellFormed("K7P2Q!") && VerificationCode.IsWellFormed("K7P2QX"), "Email code: format check");
        var session = Guid.NewGuid();
        check(VerificationCode.Hash("K7P2QX", 2, session).SequenceEqual(VerificationCode.Hash("K7P2QX", 2, session))
            && !VerificationCode.Hash("K7P2QX", 2, session).SequenceEqual(VerificationCode.Hash("K7P2QX", 2, Guid.NewGuid()))
            && !VerificationCode.Hash("K7P2QX", 2, session).SequenceEqual(VerificationCode.Hash("K7P2QX", 3, session)),
            "Email code: hash bound to account and login session");
        check(VerificationCode.MaskEmail("nguyenvana@gmail.com") == "n********a@gmail.com" && VerificationCode.MaskEmail("ab@x.vn") == "a*@x.vn", "Email: masked for display");

        check(EmailVerificationService.IsValidEmail("tenban@gmail.com") && !EmailVerificationService.IsValidEmail("tenban")
            && !EmailVerificationService.IsValidEmail("Ten <a@b.com>") && !EmailVerificationService.IsValidEmail(""), "Email: address validation");

        var service = new EmailVerificationService(new EmailVerificationStore("Server=127.0.0.1,1;Database=None"), new NoSender(),
            Options.Create(new EmailVerificationOptions()), NullLogger<EmailVerificationService>.Instance);
        ClaimsPrincipal User(string role, string? verified = null)
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "2"), new(ClaimTypes.Role, role), new(LoginSessionStore.SessionClaim, session.ToString()) };
            if (verified is not null) claims.Add(new(EmailVerificationService.VerifiedClaim, verified));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookies"));
        }
        check(!service.IsRequired(User("Manager")), "Email verification: managers are exempt");
        check(new[] { "Waiter", "Kitchen", "Cashier" }.All(r => service.IsRequired(User(r))), "Email verification: every other role must verify");
        check(!service.IsRequired(User("Waiter", session.ToString())), "Email verification: verified session passes");
        check(service.IsRequired(User("Waiter", Guid.NewGuid().ToString())), "Email verification: verification from another login session does not count");
        check(!service.IsRequired(new ClaimsPrincipal(new ClaimsIdentity())), "Email verification: anonymous visitors unaffected");
        var disabled = new EmailVerificationService(new EmailVerificationStore("x"), new NoSender(),
            Options.Create(new EmailVerificationOptions { Enabled = false }), NullLogger<EmailVerificationService>.Instance);
        check(!disabled.IsRequired(User("Waiter")), "Email verification: can be disabled by configuration");

        var mail = VerificationEmail.Create("a@b.vn", "Nguyễn <Văn> A", "K7P2QX", 10, new DateTime(2026, 10, 1, 18, 5, 0, DateTimeKind.Utc));
        check(mail.Subject.Contains("K7P2QX") && mail.Subject.StartsWith("Mã xác minh"), "Email: subject names the purpose and shows the code");
        check("K7P2QX".All(c => mail.HtmlBody.Contains($">{c}</div>")), "Email: HTML shows each character in its own large box");
        check(mail.HtmlBody.Contains("10 phút") && mail.HtmlBody.Contains("01:05 ngày 02/10/2026"), "Email: shows validity and Vietnam send time");
        check(mail.HtmlBody.Contains("Nguyễn &lt;Văn&gt; A") && !mail.HtmlBody.Contains("<Văn>"), "Email: account name is HTML-encoded");
        check(mail.TextBody.Contains("K7P2QX") && mail.TextBody.Contains("K 7 P 2 Q X"), "Email: plain-text version for apps without HTML");
        check(EmailVerificationMiddlewareAllows("/Account/XacMinhEmail") && EmailVerificationMiddlewareAllows("/account/guilaimaxacminh")
            && !EmailVerificationMiddlewareAllows("/Account/DoiMatKhau") && !EmailVerificationMiddlewareAllows("/"),
            "Email verification: only verification pages and logout are reachable before verifying (password change comes after)");
    }

    private static bool EmailVerificationMiddlewareAllows(string path) =>
        RestaurantManagement.Web.Authentication.EmailVerificationMiddleware.IsAllowed(new Microsoft.AspNetCore.Http.PathString(path));

    private sealed class NoSender : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
