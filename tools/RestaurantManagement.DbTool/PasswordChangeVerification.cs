using System.Net;

namespace RestaurantManagement.DbTool;

internal static partial class LoginVerification
{
    private static async Task VerifyRequiredPasswordChange(string connection, string password, HttpClient client,
        Func<string, string, Task<HttpResponseMessage>> login)
    {
        await DatabaseTool.Execute(connection, "UPDATE dbo.Users SET MustChangePassword=1 WHERE Id=1;");
        using var signedIn = await login("manager", password);
        Assert(signedIn.StatusCode == HttpStatusCode.Redirect && signedIn.Headers.Location!.ToString().Contains("ChangePassword"), "S1-03: first login requires a new password");
        var cookie = signedIn.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("RestaurantManagement.Auth=")).Split(';')[0];
        var newPassword = "Changed9!" + Guid.NewGuid().ToString("N");
        foreach (var path in new[] { "/", "/Management", "/Dishes", "/DishCategories", "/api/table-status" })
        {
            using var blocked = await client.GetAsync(path);
            Assert(blocked.StatusCode == HttpStatusCode.Redirect && blocked.Headers.Location!.ToString().Contains("ChangePassword"), "S1-03: direct access blocked " + path);
        }
        var form = await client.GetStringAsync("/Account/ChangePassword");
        var token = Token(form);
        using var noToken = await client.PostAsync("/Account/ChangePassword", Form(("currentPassword", password), ("newPassword", newPassword), ("confirmPassword", newPassword)));
        Assert(noToken.StatusCode == HttpStatusCode.BadRequest, "S1-03: password change requires CSRF token");
        foreach (var (current, next, confirmation) in new[] {
            ("Wrong9!", newPassword, newPassword), (password, "short1", "short1"),
            (password, "NoDigitsHere", "NoDigitsHere"), (password, password, password),
            (password, newPassword, "different") })
        {
            using var invalid = await client.PostAsync("/Account/ChangePassword", Form(("currentPassword", current), ("newPassword", next), ("confirmPassword", confirmation), ("__RequestVerificationToken", token)));
            Assert(invalid.StatusCode == HttpStatusCode.OK && (await invalid.Content.ReadAsStringAsync()).Contains("alert-danger"), "S1-03: invalid password change rejected");
        }
        await Check(connection, "SELECT CASE WHEN MustChangePassword=1 AND FailedLoginCount=0 THEN 1 ELSE 0 END FROM dbo.Users WHERE Id=1", "S1-03: rejected changes preserve required flag and login failure count");
        // An independently created second session must be revoked by the change.
        var otherSession = Guid.NewGuid();
        await DatabaseTool.Execute(connection, $"EXEC dbo.usp_CreateLoginSession @Id='{otherSession}',@UserId=1;");
        using var changed = await client.PostAsync("/Account/ChangePassword", Form(("currentPassword", password), ("newPassword", newPassword), ("confirmPassword", newPassword), ("__RequestVerificationToken", token)));
        Assert(changed.StatusCode == HttpStatusCode.Redirect, "S1-03: valid password change succeeds");
        await Check(connection, "SELECT CASE WHEN MustChangePassword=0 AND PasswordChangedAt IS NOT NULL THEN 1 ELSE 0 END FROM dbo.Users WHERE Id=1", "S1-03: required flag cleared and change time saved");
        await Check(connection, $"SELECT CASE WHEN RevokedAt IS NOT NULL THEN 1 ELSE 0 END FROM dbo.LoginSessions WHERE Id='{otherSession}'", "S1-03: other sessions revoked");
        Assert((await client.GetAsync("/Management")).IsSuccessStatusCode, "S1-03: current session remains usable after change");
        var page = await client.GetStringAsync("/Management");
        using var logout = await client.PostAsync("/Account/Logout", Form(("__RequestVerificationToken", Token(page))));
        using var oldPassword = await login("manager", password);
        Assert(oldPassword.StatusCode == HttpStatusCode.OK, "S1-03: old password no longer signs in");
        using var newLogin = await login("manager", newPassword);
        Assert(newLogin.StatusCode == HttpStatusCode.Redirect && !newLogin.Headers.Location!.ToString().Contains("ChangePassword"), "S1-03: new password signs in without forced change");
        Console.WriteLine("PASS: S1-03 required password change and session revocation.");
    }
}
