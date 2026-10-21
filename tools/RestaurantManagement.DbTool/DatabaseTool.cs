using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace RestaurantManagement.DbTool;

internal static partial class DatabaseTool
{
    internal static readonly string Root = FindRoot();
    internal static async Task Run(string[] args)
    {
        try
        {
            var command = args.FirstOrDefault() ?? "help";
            var connection = Environment.GetEnvironmentVariable("RM_CONNECTION_STRING");
            if (command == "help" || string.IsNullOrWhiteSpace(connection))
            {
                Console.WriteLine("Commands: migration-status | verify-merge-upgrade | verify-merge-tables | verify-unmerge-tables | migrate | seed-demo | reset-demo | reset-demo-accounts | seed-login-demo | seed-menu-demo | seed-menu-extra | seed-real-food-images | seed-khai-vi | seed-confirmation-demo | seed-s4-demo | seed-s4-live-demo | refresh-s4-live-demo | seed-menu-200 | hide-menu-200 | email-retry-now | verify | verify-s410-task3 | verify-booking-email | verify-api-permissions | verify-temporary-out | verify-qr-session | verify-late-reservations | verify-hold-extension | verify-phone-no-show | verify-cancel-item | verify-cancel-report | verify-charged-cancel | verify-bill-preview | verify-bill-header | verify-bill-print | verify-bill-pdf | maintenance | check\nSet RM_CONNECTION_STRING first. seed-demo, reset-demo-accounts and seed-login-demo also require RM_DEMO_PASSWORD.\nreset-demo restores only a previously seeded _Dev, _Demo, _Staging or _Test_ database; it preserves password hashes and retained audit history.\nseed-login-demo accepts RM_DEMO_USERNAME and RM_DEMO_PHONE; existing accounts are preserved.\nseed-s4-live-demo only adds the S4 live rehearsal records and leaves existing data untouched. refresh-s4-live-demo moves only the two active S4 rehearsal bookings forward by seven days.\nverify and verify-s410-task3 create and remove their own uniquely named test database.");
                Environment.ExitCode = command == "help" ? 0 : 1;
                return;
            }
            switch (command)
            {
                case "migration-status": await MigrationStatus(connection); break;
                case "verify-merge-upgrade": await TableMergeUpgradeVerification.Run(connection); break;
                case "verify-unmerge-tables": await TableUnmergeVerification.Run(connection); break;
                case "verify-merge-tables": await TableMergeVerification.Run(connection); break;
                case "migrate": await Migrate(connection); break;
                case "seed-demo": await Seed(connection); break;
                case "reset-demo": await DemoDataReset.Run(connection); break;
                case "reset-demo-accounts": await ResetDemoAccounts(connection); break;
                case "seed-login-demo": await SeedLoginDemo(connection); break;
                case "seed-menu-demo": await SeedMenuDemo(connection); break;
                case "seed-menu-extra": await SeedMenuExtra(connection); break;
                case "seed-real-food-images": await SeedRealFoodImages(connection); break;
                case "seed-khai-vi": await SeedKhaiVi(connection); break;
                case "seed-confirmation-demo": await SeedConfirmationDemo(connection); break;
                case "seed-s4-demo": await SeedS4Demo(connection); break;
                case "seed-s4-live-demo": await SeedS4LiveDemo(connection); break;
                case "refresh-s4-live-demo": await RefreshS4LiveDemo(connection); break;
                case "seed-menu-200": await SeedMenu200(connection); break;
                case "hide-menu-200": await HideMenu200(connection); break;
                case "verify": await Verification.Run(connection); break;
                case "verify-s410-task3": await Verification.RunS410Task3(connection); break;
                // S2-09 Task 3: chỉ nghiệm thu luồng đặt bàn + email (database tạm, cần build Debug trước).
                case "verify-booking-email": await Verification.RunBookingEmail(connection); break;
                // S1-04 Task 4: chỉ kiểm thử phân quyền toàn bộ API theo vai trò (database tạm, cần build Debug trước).
                case "verify-api-permissions": await Verification.RunApiPermissions(connection); break;
                // S2-08 Task 4: nghiệm thu toàn bộ luồng "Tạm hết" (database tạm, cần build Debug trước).
                case "verify-temporary-out": await Verification.RunTemporaryOut(connection); break;
                // S3-01 Task 1: quét QR bàn trống → mở phiên gọi món mới (database tạm, cần build Debug trước).
                case "verify-qr-session": await Verification.RunQrSession(connection); break;
                // S3-09 Task 1: cảnh báo đặt bàn trễ 15 phút + đánh dấu khách không tới (database tạm, cần build Debug trước).
                case "verify-late-reservations": await Verification.RunLateReservations(connection); break;
                // S3-09 Task 2: gia hạn giữ bàn 15 phút (cùng bộ kiểm thử với Task 1, database tạm, cần build Debug trước).
                case "verify-hold-extension": await Verification.RunLateReservations(connection); break;
                // S3-09 Task 3: cảnh báo số điện thoại có ít nhất 3 lần không tới trong 90 ngày (cùng bộ kiểm thử S3-09).
                case "verify-phone-no-show": await Verification.RunLateReservations(connection); break;
                // S3-05 Task 1: huỷ dòng món chờ bếp với lý do bắt buộc (database tạm, cần build Debug trước).
                case "verify-cancel-item": await Verification.RunCancelItem(connection); break;
                // S3-05 Task 2: báo cáo cuối ca — các lần huỷ món của ca, chỉ Quản lý (database tạm, cần build Debug trước).
                case "verify-cancel-report": await Verification.RunCancelReport(connection); break;
                // S3-05 Task 3: Quản lý huỷ món đã bắt đầu chế biến, món vẫn tính tiền (database tạm, cần build Debug trước).
                case "verify-charged-cancel": await Verification.RunChargedCancel(connection); break;
                // S4-01 Task 1: màn hình "Tạm tính" của bàn đang phục vụ, chỉ đọc (database tạm, cần build Debug trước).
                case "verify-bill-preview": await Verification.RunBillPreview(connection); break;
                // S4-01 Task 2: phần đầu hoá đơn tạm tính (chạy cùng bộ kiểm thử Task 1, database tạm, cần build Debug trước).
                case "verify-bill-header": await Verification.RunBillPreview(connection); break;
                // S4-01 Task 3: in hoá đơn tạm tính khổ 80mm (chạy cùng bộ kiểm thử S4-01, database tạm, cần build Debug trước).
                case "verify-bill-print": await Verification.RunBillPreview(connection); break;
                // S4-01 Task 4: xuất PDF hoá đơn tạm tính khổ 80mm (chạy cùng bộ kiểm thử S4-01, database tạm, cần build Debug trước).
                case "verify-bill-pdf": await Verification.RunBillPreview(connection); break;
                case "maintenance": await Execute(connection, "EXEC dbo.usp_RunMaintenance;"); break;
                // S2-09 Task 2 (demo): không chờ 5 phút, cho các email đặt bàn đang chờ gửi lại tới giờ gửi lại ngay.
                case "email-retry-now": await EmailRetryNow(connection); break;
                case "check": await Execute(connection, "SELECT TOP(1) Name FROM dbo.SchemaVersions;"); Console.WriteLine("Database connected."); break;
                default: throw new ArgumentException("Unknown command.");
            }
        }
        catch (Exception ex) { Console.Error.WriteLine($"Operation failed ({ex.GetType().Name}): {ex.Message}"); Environment.ExitCode = 1; }
    }

    /// <summary>Demo S2-09 Task 2: đưa giờ gửi lại của email đặt bàn đang chờ thử lại về hiện tại (web gửi lại trong vòng 30 giây).</summary>
    internal static async Task EmailRetryNow(string connection)
    {
        await using var cn = new SqlConnection(connection);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("""
            UPDATE dbo.EmailOutbox SET NextAttemptAt=SYSUTCDATETIME()
            WHERE Status='Pending' AND AttemptCount BETWEEN 1 AND 3 AND MessageType IN ('BookingReceived','BookingConfirmed','BookingCancelled');
            SELECT @@ROWCOUNT;
            """, cn);
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        Console.WriteLine($"{count} email đặt bàn sẽ được gửi lại ở lượt kiểm tra kế tiếp của web (tối đa 30 giây).");
    }

    internal static async Task Migrate(string connection, string? throughMigration = null)
    {
        var builder = new SqlConnectionStringBuilder(connection);
        var database = builder.InitialCatalog;
        if (!DatabaseName().IsMatch(database) || new[] { "master", "model", "msdb", "tempdb" }.Contains(database, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Use a dedicated database name with letters, digits and underscores.");
        // Azure Portal can provision an empty database before this tool runs.
        // Connecting to that database is the reliable existence check: querying
        // DB_ID from Azure's master database can be denied or return NULL even
        // though the caller can connect to the target database.
        var databaseAlreadyExists = false;
        try
        {
            await using var target = new SqlConnection(connection);
            await target.OpenAsync();
            databaseAlreadyExists = true;
        }
        catch (SqlException ex) when (ex.Number == 4060)
        {
            // The local-development flow creates a dedicated database below.
        }

        if (!databaseAlreadyExists)
        {
            builder.InitialCatalog = "master";
            await using var master = new SqlConnection(builder.ConnectionString);
            await master.OpenAsync();
            await using var cmd = new SqlCommand($"IF DB_ID(@name) IS NULL CREATE DATABASE [{database}];", master);
            cmd.Parameters.AddWithValue("@name", database);
            await cmd.ExecuteNonQueryAsync();
        }
        await using var cn = new SqlConnection(connection);
        await cn.OpenAsync();
        await using (var cmd = new SqlCommand("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource='RestaurantSchemaMigration',@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=30000; IF @r<0 THROW 51300,'Migration lock unavailable.',1;", cn))
            await cmd.ExecuteNonQueryAsync();
        try
        {
            await using (var cmd = new SqlCommand("IF OBJECT_ID('dbo.SchemaVersions') IS NULL CREATE TABLE dbo.SchemaVersions(Name nvarchar(200) NOT NULL PRIMARY KEY,Sha256 char(64) NOT NULL,AppliedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME());", cn))
                await cmd.ExecuteNonQueryAsync();
            foreach (var path in Directory.GetFiles(Path.Combine(Root, "database", "migrations"), "*.sql").Order(StringComparer.Ordinal))
            {
                var name = Path.GetFileName(path);
                if (throughMigration is not null && StringComparer.Ordinal.Compare(name, throughMigration)>0)
                    continue;
                var sql = (await File.ReadAllTextAsync(path)).Replace("\r\n", "\n");
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql)));
                await using var lookup = new SqlCommand("SELECT Sha256 FROM dbo.SchemaVersions WHERE Name=@name;", cn);
                lookup.Parameters.AddWithValue("@name", name);
                var existing = await lookup.ExecuteScalarAsync() as string;
                if (existing is not null)
                {
                    if (existing != hash) throw new InvalidOperationException($"Applied migration {name} has changed. Add a new numbered migration.");
                    Console.WriteLine($"Unchanged: {name}");
                    continue;
                }
                await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
                try
                {
                    await Batches(cn, tx, sql);
                    await using var insert = new SqlCommand("INSERT dbo.SchemaVersions(Name,Sha256) VALUES(@name,@hash);", cn, tx);
                    insert.Parameters.AddWithValue("@name", name);
                    insert.Parameters.AddWithValue("@hash", hash);
                    await insert.ExecuteNonQueryAsync();
                    await tx.CommitAsync();
                    Console.WriteLine($"Applied: {name}");
                }
                catch { await tx.RollbackAsync(); throw; }
            }
        }
        finally
        {
            await using var cmd = new SqlCommand("EXEC sys.sp_releaseapplock @Resource='RestaurantSchemaMigration',@LockOwner='Session';", cn);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    internal static async Task Seed(string connection, string? testPassword = null)
    {
        var password = testPassword ?? Environment.GetEnvironmentVariable("RM_DEMO_PASSWORD");
        if (password is null || password.Length < 8 || !password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            throw new ArgumentException("RM_DEMO_PASSWORD needs 8+ characters, including a letter and a digit.");
        await using var cn = new SqlConnection(connection);
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
        try
        {
            await using var check = new SqlCommand("SELECT COUNT(*) FROM dbo.SchemaVersions WHERE Name='DEMO_DATA';", cn, tx);
            if (Convert.ToInt32(await check.ExecuteScalarAsync()) > 0)
            {
                Console.WriteLine("Demo already seeded; existing data preserved.");
                await tx.RollbackAsync();
                return;
            }
            await Batches(cn, tx, await File.ReadAllTextAsync(Path.Combine(Root, "database", "seeds", "Demo.sql")));
            foreach (var user in new[] { "manager", "waiter", "kitchen", "cashier" })
            {
                await using var cmd = new SqlCommand("UPDATE dbo.Users SET PasswordHash=@hash,IsActive=1 WHERE UserName=@name;", cn, tx);
                cmd.Parameters.AddWithValue("@hash", BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12));
                cmd.Parameters.AddWithValue("@name", user);
                await cmd.ExecuteNonQueryAsync();
            }
            await using var marker = new SqlCommand("INSERT dbo.SchemaVersions(Name,Sha256) VALUES('DEMO_DATA',REPLICATE('0',64));", cn, tx);
            await marker.ExecuteNonQueryAsync();
            await tx.CommitAsync();
            Console.WriteLine("Demo seeded: 4 accounts, 3 areas, 25 tables, 60 menu items, 20 bookings. Password not logged.");
        }
        catch { await tx.RollbackAsync(); throw; }
    }

    /// <summary>
    /// Đồng bộ đúng bốn tài khoản diễn tập chuẩn, không tạo tài khoản mới và không
    /// thay đổi vai trò/email. Dùng khi database demo đã có dữ liệu nhưng mật khẩu
    /// cũ không còn biết; mật khẩu chỉ nhận từ biến môi trường, không ghi ra console.
    /// </summary>
    internal static async Task ResetDemoAccounts(string connection)
    {
        var password = Environment.GetEnvironmentVariable("RM_DEMO_PASSWORD");
        if (password is null || password.Length < 8 || !password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            throw new ArgumentException("RM_DEMO_PASSWORD needs 8+ characters, including a letter and a digit.");

        var accounts = new[] { "manager", "waiter", "kitchen", "cashier" };
        await using var cn = new SqlConnection(connection);
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
        try
        {
            foreach (var account in accounts)
            {
                await using var command = new SqlCommand("""
                    UPDATE dbo.Users
                    SET PasswordHash=@hash, IsActive=1, MustChangePassword=0,
                        FailedLoginCount=0, FailureWindowStartedAt=NULL, LockedUntil=NULL,
                        PasswordChangedAt=SYSUTCDATETIME(), UpdatedAt=SYSUTCDATETIME()
                    WHERE UserName=@userName;
                    """, cn, tx);
                command.Parameters.AddWithValue("@hash", BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12));
                command.Parameters.AddWithValue("@userName", account);
                if (await command.ExecuteNonQueryAsync() != 1)
                    throw new InvalidOperationException($"Missing or ambiguous demo account: {account}.");
            }
            await tx.CommitAsync();
            Console.WriteLine("Demo accounts updated: manager, waiter, kitchen, cashier.");
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    internal static async Task Execute(string connection, string sql)
    {
        await using var cn = new SqlConnection(connection);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 60 };
        await cmd.ExecuteNonQueryAsync();
    }
    private static async Task Batches(SqlConnection cn, SqlTransaction tx, string sql)
    {
        foreach (var batch in GoSeparator().Split(sql).Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            await using var cmd = new SqlCommand(batch, cn, tx) { CommandTimeout = 120 };
            await cmd.ExecuteNonQueryAsync();
        }
    }
    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "database", "migrations"))) return dir.FullName;
        throw new InvalidOperationException("Run DbTool from repository root.");
    }
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]{0,100}$")] private static partial Regex DatabaseName();
    [GeneratedRegex(@"^\s*GO\s*(?:--[^\r\n]*)?$", RegexOptions.Multiline | RegexOptions.IgnoreCase)] private static partial Regex GoSeparator();
}
