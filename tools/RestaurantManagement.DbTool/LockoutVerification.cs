using System.Net;
using System.Text.RegularExpressions;

namespace RestaurantManagement.DbTool;

internal static partial class LoginVerification
{
    private static async Task VerifyLockout(string connection, string password, HttpClient client,
        Func<string, string, Task<HttpResponseMessage>> login)
    {
        const string error = "Tên đăng nhập, số điện thoại hoặc mật khẩu không hợp lệ.";
        async Task<string> Failed(string identifier, string secret)
        {
            using var response = await login(identifier, secret);
            Assert(response.StatusCode == HttpStatusCode.OK, "Rejected login returns form");
            var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
            Assert(html.Contains(error), "Rejected login uses generic error");
            Assert((await client.GetAsync("/Management")).StatusCode == HttpStatusCode.Redirect, "Rejected login has no session");
            return html;
        }
        async Task Logout()
        {
            var page = await client.GetStringAsync("/Management");
            using var response = await client.PostAsync("/Account/Logout", Form(("__RequestVerificationToken", Token(page))));
            Assert(response.StatusCode == HttpStatusCode.Redirect, "Test session logged out");
        }
        static int Seconds(string html)
        {
            var match = Regex.Match(html, "data-seconds=\"(\\d+)\"");
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }
        using (var initial = await login("manager", password))
            Assert(initial.StatusCode == HttpStatusCode.Redirect, "Start lockout tests with a clean authenticated account");
        await Logout();
        for (var i = 1; i <= 4; i++)
        {
            var html = await Failed(i % 2 == 0 ? "0900000001" : "manager", "WrongPassword9!");
            Assert(Seconds(html) == 0, $"Attempt {i} allows retry");
        }
        await Check(connection, "SELECT CASE WHEN (SELECT COUNT(*) FROM dbo.LoginFailures WHERE SubjectKey='U:1' AND CountsTowardLimit=1)=4 AND (SELECT FailedLoginCount FROM dbo.Users WHERE Id=1)=4 THEN 1 ELSE 0 END", "Each failure timestamp saved; username and phone share count");
        using (var success = await login("manager", password))
            Assert(success.StatusCode == HttpStatusCode.Redirect, "Correct password after four failures succeeds");
        await Check(connection, "SELECT CASE WHEN NOT EXISTS(SELECT 1 FROM dbo.LoginFailures WHERE SubjectKey='U:1') AND EXISTS(SELECT 1 FROM dbo.Users WHERE Id=1 AND FailedLoginCount=0 AND LockedUntil IS NULL AND FailureWindowStartedAt IS NULL) THEN 1 ELSE 0 END", "Success clears all failure state");
        await Logout();

        for (var i = 1; i <= 5; i++)
        {
            var known = await Failed("manager", "WrongPassword9!");
            var unknown = await Failed("unknown-lockout-demo", "WrongPassword9!");
            Assert((Seconds(known) > 0) == (Seconds(unknown) > 0), "Unknown identifier exposes the same waiting behavior");
            Assert(i < 5 ? Seconds(known) == 0 : Seconds(known) is > 895 and <= 900,
                $"Lock begins only at fifth failure (attempt {i})");
        }
        await Check(connection, "SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.Users WHERE Id=1 AND FailedLoginCount=5 AND LockedUntil BETWEEN DATEADD(second,880,SYSUTCDATETIME()) AND DATEADD(second,900,SYSUTCDATETIME())) THEN 1 ELSE 0 END", "Fifth failure locks for fifteen minutes");
        await Failed("0900000001", password);
        await Check(connection, "SELECT CASE WHEN (SELECT FailedLoginCount FROM dbo.Users WHERE Id=1)=5 AND (SELECT COUNT(*) FROM dbo.LoginFailures WHERE SubjectKey='U:1' AND CountsTowardLimit=0)=1 THEN 1 ELSE 0 END", "Correct password during lock cannot sign in or extend the failure count");

        // Move fixture timestamps instead of waiting fifteen minutes; production has no clock override.
        await DatabaseTool.Execute(connection, "UPDATE dbo.LoginLockoutSubjects SET LockedUntil=DATEADD(second,45,SYSUTCDATETIME()) WHERE SubjectKey='U:1'; UPDATE dbo.Users SET LockedUntil=(SELECT LockedUntil FROM dbo.LoginLockoutSubjects WHERE SubjectKey='U:1') WHERE Id=1;");
        var remaining = await Failed("manager", password);
        Assert(Seconds(remaining) is > 0 and <= 45 && remaining.Contains("login-lockout.js"), "UI renders remaining time and countdown script");
        await Check(connection, "SELECT CASE WHEN DATEDIFF(second,SYSUTCDATETIME(),LockedUntil)<=45 THEN 1 ELSE 0 END FROM dbo.LoginLockoutSubjects WHERE SubjectKey='U:1'", "Blocked requests do not extend the lock");
        await DatabaseTool.Execute(connection, "UPDATE dbo.LoginLockoutSubjects SET LockedUntil=DATEADD(second,-1,SYSUTCDATETIME()) WHERE SubjectKey='U:1'; UPDATE dbo.Users SET LockedUntil=DATEADD(second,-1,SYSUTCDATETIME()) WHERE Id=1;");
        using (var success = await login("0900000001", password))
            Assert(success.StatusCode == HttpStatusCode.Redirect, "Expired lock allows correct password");
        await Logout();

        await DatabaseTool.Execute(connection, "INSERT dbo.LoginFailures(SubjectKey,FailedAt,CountsTowardLimit) VALUES('U:1',DATEADD(minute,-16,SYSUTCDATETIME()),1),('U:1',DATEADD(minute,-15,SYSUTCDATETIME()),1),('U:1',DATEADD(minute,-14,SYSUTCDATETIME()),1),('U:1',DATEADD(minute,-1,SYSUTCDATETIME()),1);");
        Assert(Seconds(await Failed("manager", "WrongPassword9!")) == 0, "Old failures do not cause a lock");
        await Check(connection, "SELECT CASE WHEN (SELECT COUNT(*) FROM dbo.LoginFailures WHERE SubjectKey='U:1')=3 AND (SELECT FailedLoginCount FROM dbo.Users WHERE Id=1)=3 THEN 1 ELSE 0 END", "Sliding window excludes failures at or before fifteen minutes ago");
        using (var success = await login("manager", password))
            Assert(success.StatusCode == HttpStatusCode.Redirect, "Success resets sliding-window failures");
        await Logout();

        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => DatabaseTool.Execute(connection,
            "EXEC dbo.usp_CompleteLogin @UserId=1,@Identifier=N'manager',@PasswordValid=0;")));
        await Check(connection, "SELECT CASE WHEN (SELECT COUNT(*) FROM dbo.LoginFailures WHERE SubjectKey='U:1' AND CountsTowardLimit=1)=5 AND (SELECT COUNT(*) FROM dbo.LoginFailures WHERE SubjectKey='U:1' AND CountsTowardLimit=0)=5 AND (SELECT FailedLoginCount FROM dbo.Users WHERE Id=1)=5 THEN 1 ELSE 0 END", "Ten concurrent failures cannot bypass threshold or extend lock");
        await DatabaseTool.Execute(connection, "UPDATE dbo.LoginLockoutSubjects SET LockedUntil=DATEADD(second,-1,SYSUTCDATETIME()) WHERE SubjectKey='U:1'; UPDATE dbo.Users SET LockedUntil=DATEADD(second,-1,SYSUTCDATETIME()) WHERE Id=1;");
        Assert(Seconds(await Failed("manager", "WrongPassword9!")) == 0, "First wrong password after expiry starts a fresh window");
        await Check(connection, "SELECT CASE WHEN FailedLoginCount=1 AND LockedUntil IS NULL THEN 1 ELSE 0 END FROM dbo.Users WHERE Id=1", "Expired lock resets old failures");
        using (var success = await login("manager", password))
            Assert(success.StatusCode == HttpStatusCode.Redirect, "Final login succeeds");
        await Logout();
        Console.WriteLine("PASS: S1-01 Task 2 rolling-window lockout, countdown, privacy and concurrency checks.");
    }
}
