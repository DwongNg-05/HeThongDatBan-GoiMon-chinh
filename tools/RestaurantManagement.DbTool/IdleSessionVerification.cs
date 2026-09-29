using System.Net;
using Microsoft.Data.SqlClient;

namespace RestaurantManagement.DbTool;

internal static partial class LoginVerification
{
    private static async Task VerifyIdleSessions(string connection, string password, HttpClient client,
        Func<string, string, Task<HttpResponseMessage>> login)
    {
        async Task<object> Scalar(string sql)
        {
            await using var cn = new SqlConnection(connection);
            await cn.OpenAsync();
            await using var command = new SqlCommand(sql, cn);
            return (await command.ExecuteScalarAsync())!;
        }
        async Task<Guid> CurrentSession() => (Guid)await Scalar("SELECT TOP(1) Id FROM dbo.LoginSessions WHERE UserId=1 AND RevokedAt IS NULL ORDER BY CreatedAt DESC");
        async Task Age(Guid id, int seconds) => await DatabaseTool.Execute(connection,
            $"UPDATE dbo.LoginSessions SET LastActivityAt=DATEADD(second,-{seconds},SYSUTCDATETIME()) WHERE Id='{id}';");
        async Task<DateTime> Last(Guid id) => (DateTime)await Scalar($"SELECT LastActivityAt FROM dbo.LoginSessions WHERE Id='{id}'");
        static string Cookie(HttpResponseMessage response) => response.Headers.GetValues("Set-Cookie")
            .Single(c => c.StartsWith("RestaurantManagement.Auth=")).Split(';')[0];
        using var replayHandler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        using var replay = new HttpClient(replayHandler) { BaseAddress = client.BaseAddress };
        async Task AssertReplayDenied(string cookie)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/Management");
            request.Headers.Add("Cookie", cookie);
            using var result = await replay.SendAsync(request);
            Assert(result.StatusCode == HttpStatusCode.Redirect && result.Headers.Location!.ToString().Contains("/Account/Login"), "Revoked cookie replay cannot open management");
        }

        using var initial = await login("manager", password);
        Assert(initial.StatusCode == HttpStatusCode.Redirect, "Login creates an idle-tracked session");
        var firstCookie = Cookie(initial);
        var id = await CurrentSession();
        await Check(connection, $"SELECT CASE WHEN UserId=1 AND LastActivityAt=CreatedAt AND RevokedAt IS NULL THEN 1 ELSE 0 END FROM dbo.LoginSessions WHERE Id='{id}'", "New session stores correct user and initial activity");

        await Age(id, 29 * 60);
        var before = await Last(id);
        var page = await client.GetStringAsync("/Management");
        Assert(page.Contains("manager"), "Session before thirty minutes can access management");
        Assert(await Last(id) > before, "Valid management read refreshes activity");
        var token = Token(page);

        await Age(id, 20 * 60);
        before = await Last(id);
        using (var assets = await client.GetAsync("/css/site.css"))
            Assert(assets.IsSuccessStatusCode, "Static asset request succeeds");
        using (var publicPage = await client.GetAsync("/Home/Privacy"))
            Assert(publicPage.IsSuccessStatusCode, "Public page request succeeds");
        Assert(await Last(id) == before, "Static assets and public pages do not refresh activity");
        using (var invalid = await client.PostAsync("/Management/Price", Form(("id", "60"), ("price", "-1"), ("__RequestVerificationToken", token))))
            Assert(invalid.StatusCode == HttpStatusCode.Redirect, "Invalid price is rejected with feedback");
        Assert(await Last(id) == before, "Invalid input does not refresh activity");
        using (var csrf = await client.PostAsync("/Management/Price", Form(("id", "60"), ("price", "87656"))))
            Assert(csrf.StatusCode == HttpStatusCode.BadRequest, "Missing CSRF token is rejected");
        Assert(await Last(id) == before, "Rejected CSRF request does not refresh activity");
        using (var change = await client.PostAsync("/Management/Price", Form(("id", "60"), ("price", "87656"), ("__RequestVerificationToken", token))))
            Assert(change.StatusCode == HttpStatusCode.Redirect, "Valid menu change succeeds");
        Assert(await Last(id) > before, "Valid menu change refreshes activity");

        await Age(id, 30 * 60);
        using (var expired = await client.GetAsync("/Management"))
        {
            Assert(expired.StatusCode == HttpStatusCode.Redirect && expired.Headers.Location!.ToString().Contains("sessionExpired=true"), "Thirty idle minutes redirects to login with expiration reason");
            var html = WebUtility.HtmlDecode(await client.GetStringAsync(expired.Headers.Location));
            Assert(html.Contains("Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại."), "Login page explains session expiration");
        }
        await Check(connection, $"SELECT CASE WHEN RevokedAt IS NOT NULL THEN 1 ELSE 0 END FROM dbo.LoginSessions WHERE Id='{id}'", "Idle session is permanently revoked on the server");
        await AssertReplayDenied(firstCookie);
        // Even changing the timestamp cannot resurrect a revoked session.
        await Age(id, 0);
        await AssertReplayDenied(firstCookie);

        using var again = await login("0900000001", password);
        Assert(again.StatusCode == HttpStatusCode.Redirect, "Login after expiration succeeds by phone");
        var newId = await CurrentSession();
        var newCookie = Cookie(again);
        Assert(newId != id && newCookie != firstCookie, "Re-login creates a new session and cookie");
        page = await client.GetStringAsync("/Management");
        token = Token(page);
        var priceBefore = await Scalar("SELECT Price FROM dbo.MenuItems WHERE Id=60");
        await Age(newId, 31 * 60);
        using (var denied = await client.PostAsync("/Management/Price", Form(("id", "60"), ("price", "87657"), ("__RequestVerificationToken", token))))
            Assert(denied.StatusCode == HttpStatusCode.Redirect && denied.Headers.Location!.ToString().Contains("/Account/Login"), "Expired POST cannot execute a protected action");
        Assert(Equals(priceBefore, await Scalar("SELECT Price FROM dbo.MenuItems WHERE Id=60")), "Expired request cannot change menu data");

        using (var publicExpiryLogin = await login("manager", password))
            Assert(publicExpiryLogin.StatusCode == HttpStatusCode.Redirect, "Create session for public-page expiration check");
        await Age(await CurrentSession(), 31 * 60);
        using (var publicExpired = await client.GetAsync("/Home/Privacy"))
            Assert(publicExpired.IsSuccessStatusCode, "Public page remains available after session expiration");
        using (var management = await client.GetAsync("/Management"))
        {
            Assert(management.StatusCode == HttpStatusCode.Redirect, "Access after public-page expiry still requires login");
            var html = WebUtility.HtmlDecode(await client.GetStringAsync(management.Headers.Location));
            Assert(html.Contains("Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại."), "Expiration notice survives a preceding public request");
        }

        using var finalLogin = await login("manager", password);
        Assert(finalLogin.StatusCode == HttpStatusCode.Redirect, "Another fresh login succeeds");
        var finalCookie = Cookie(finalLogin);
        page = await client.GetStringAsync("/Management");
        using (var logout = await client.PostAsync("/Account/Logout", Form(("__RequestVerificationToken", Token(page)))))
            Assert(logout.StatusCode == HttpStatusCode.Redirect && !logout.Headers.Location!.ToString().Contains("sessionExpired"), "Explicit logout is not reported as idle expiration");
        await AssertReplayDenied(finalCookie);
        Console.WriteLine("PASS: S1-01 Task 3 idle timeout, valid activity, expired-cookie replay and re-login checks.");
    }
}
