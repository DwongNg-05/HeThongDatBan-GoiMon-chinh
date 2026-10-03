using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models;

namespace RestaurantManagement.Web.Controllers;

public sealed class AccountController(IConfiguration configuration) : Controller
{
    private string ConnectionString => Environment.GetEnvironmentVariable("RM_CONNECTION_STRING")
        ?? configuration.GetConnectionString("RestaurantManagement")
        ?? throw new InvalidOperationException("Set RM_CONNECTION_STRING to connect to SQL Server.");

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginInput());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginInput input, string? returnUrl, CancellationToken cancellationToken)
    {
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid) return View(input);
        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            int? id = null;
            string? name = null, role = null, hash = null;
            bool active = false, locked = false;
            await using (var command = new SqlCommand("SELECT u.Id,u.FullName,r.Code,u.PasswordHash,u.IsActive,CONVERT(bit,CASE WHEN u.LockedUntil>SYSUTCDATETIME() THEN 1 ELSE 0 END) FROM dbo.Users u WITH(UPDLOCK,HOLDLOCK) JOIN dbo.Roles r ON r.Id=u.RoleId WHERE u.NormalizedUserName=UPPER(LTRIM(RTRIM(@name)));", connection, transaction))
            {
                command.Parameters.Add("@name", SqlDbType.NVarChar, 50).Value = input.UserName.Trim();
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    id = reader.GetInt32(0); name = reader.GetString(1); role = reader.GetString(2);
                    hash = reader.IsDBNull(3) ? null : reader.GetString(3); active = reader.GetBoolean(4); locked = reader.GetBoolean(5);
                }
            }

            var passwordMatches = hash is not null && !locked && BCrypt.Net.BCrypt.Verify(input.Password, hash);
            var succeeded = passwordMatches && active;
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            if (succeeded)
            {
                await using var reset = new SqlCommand("UPDATE dbo.Users SET FailedLoginCount=0,FailureWindowStartedAt=NULL,LockedUntil=NULL,LastLoginAt=SYSUTCDATETIME() WHERE Id=@id; INSERT dbo.LoginAttempts(UserId,UserNameAttempt,IpAddress,Succeeded) VALUES(@id,@name,@ip,1);", connection, transaction);
                reset.Parameters.Add("@id", SqlDbType.Int).Value = id!.Value;
                reset.Parameters.Add("@name", SqlDbType.NVarChar, 50).Value = input.UserName.Trim();
                reset.Parameters.Add("@ip", SqlDbType.VarChar, 45).Value = ip;
                await reset.ExecuteNonQueryAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                var claims = new[] { new Claim(ClaimTypes.NameIdentifier, id.Value.ToString()), new Claim(ClaimTypes.Name, name!), new Claim(ClaimTypes.Role, role!) };
                var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "StaffCookie"));
                await HttpContext.SignInAsync("StaffCookie", principal, new AuthenticationProperties { IsPersistent = false, AllowRefresh = true });
                return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl!) : RedirectToAction("Index", "Menu");
            }

            await using (var fail = new SqlCommand("IF @id IS NOT NULL AND (LockedUntil IS NULL OR LockedUntil<=SYSUTCDATETIME()) UPDATE dbo.Users SET FailedLoginCount=CASE WHEN FailureWindowStartedAt IS NULL OR FailureWindowStartedAt<DATEADD(minute,-15,SYSUTCDATETIME()) THEN 1 ELSE FailedLoginCount+1 END,FailureWindowStartedAt=CASE WHEN FailureWindowStartedAt IS NULL OR FailureWindowStartedAt<DATEADD(minute,-15,SYSUTCDATETIME()) THEN SYSUTCDATETIME() ELSE FailureWindowStartedAt END,LockedUntil=CASE WHEN FailureWindowStartedAt>=DATEADD(minute,-15,SYSUTCDATETIME()) AND FailedLoginCount>=4 THEN DATEADD(minute,15,SYSUTCDATETIME()) ELSE LockedUntil END WHERE Id=@id; INSERT dbo.LoginAttempts(UserId,UserNameAttempt,IpAddress,Succeeded) VALUES(@id,@name,@ip,0);", connection, transaction))
            {
                fail.Parameters.Add("@id", SqlDbType.Int).Value = (object?)id ?? DBNull.Value;
                fail.Parameters.Add("@name", SqlDbType.NVarChar, 50).Value = input.UserName.Trim();
                fail.Parameters.Add("@ip", SqlDbType.VarChar, 45).Value = ip;
                await fail.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            ModelState.AddModelError(string.Empty, "Tên đăng nhập hoặc mật khẩu không đúng, hoặc tài khoản đang tạm khóa.");
            return View(input);
        }
        catch (SqlException)
        {
            ModelState.AddModelError(string.Empty, "Chưa kết nối được cơ sở dữ liệu. Hãy kiểm tra SQL Server và RM_CONNECTION_STRING.");
            return View(input);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(input);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync("StaffCookie");
        return RedirectToAction("Index", "Menu");
    }

    public IActionResult Forbidden() { Response.StatusCode = StatusCodes.Status403Forbidden; return View(); }
}
