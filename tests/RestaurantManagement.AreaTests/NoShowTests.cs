using Microsoft.Data.SqlClient;
using RestaurantManagement.DbTool;

internal static class NoShowTests
{
    public static async Task Run(Action<bool, string> check)
    {
        var source = Environment.GetEnvironmentVariable("RM_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Set RM_CONNECTION_STRING to the local test SQL server.");
        var b = new SqlConnectionStringBuilder(source);
        var name = "RestaurantNoShowTest_" + Guid.NewGuid().ToString("N");
        b.InitialCatalog = name;
        var cs = b.ConnectionString;
        async Task<object?> Sql(string sql)
        {
            await using var c = new SqlConnection(cs); await c.OpenAsync();
            await using var cmd = new SqlCommand(sql, c) { CommandTimeout = 60 };
            return await cmd.ExecuteScalarAsync();
        }
        async Task<int> Mark(long id)
        {
            try { await Sql($"EXEC dbo.usp_RecordReservationNoShow {id},1;"); return 0; }
            catch (SqlException e) { return e.Number; }
        }
        var sequence = 0;
        async Task<long> Booking(int minutes, string status = "Confirmed")
        {
            var code = "N" + (++sequence).ToString("D5");
            return Convert.ToInt64(await Sql($"""
                INSERT dbo.DiningTables(AreaId,Code,MaxCapacity,Status)
                SELECT TOP(1) Id,'{code}',4,'Reserved' FROM dbo.Areas;
                DECLARE @table int=SCOPE_IDENTITY(),@start datetime2(3)=DATEADD(minute,{-minutes},SYSUTCDATETIME());
                INSERT dbo.Reservations(Code,CustomerName,Phone,GuestCount,TableId,StartsAt,EndsAt,Status)
                VALUES('{code}',N'No-show test','0912345678',2,@table,@start,DATEADD(minute,90,@start),'{status}');
                SELECT CONVERT(bigint,SCOPE_IDENTITY());
                """));
        }
        try
        {
            await DatabaseTool.Migrate(cs);
            await Sql("INSERT dbo.Users(RoleId,FullName,UserName,Phone) VALUES(1,N'Test','no_show_test','0900000099');");
            foreach (var (offset, expected) in new[] { (899999, 0), (900000, 1), (900001, 1) })
                check(Convert.ToInt32(await Sql($"DECLARE @s datetime2(3)='2026-01-01'; SELECT dbo.fn_IsNoShowDue('Confirmed',NULL,@s,DATEADD(millisecond,{offset},@s));")) == expected,
                    $"15-minute boundary at {offset}ms");
            check(Convert.ToInt32(await Sql("SELECT dbo.fn_IsNoShowDue('Confirmed',SYSUTCDATETIME(),DATEADD(hour,-1,SYSUTCDATETIME()),SYSUTCDATETIME());")) == 0, "Arrival timestamp excludes warning");
            foreach (var status in new[] { "Pending", "Arrived", "Rejected", "Cancelled", "NoShow" })
                check(Convert.ToInt32(await Sql($"SELECT dbo.fn_IsNoShowDue('{status}',NULL,DATEADD(hour,-1,SYSUTCDATETIME()),SYSUTCDATETIME());")) == 0, $"No warning for {status}");
            foreach (var phone in new[] { "0912345678", "+84 912.345.678", "0084(912)-345-678" })
                check((string?)await Sql($"SELECT dbo.fn_NoShowPhone('{phone}');") == "0912345678", "Normalize " + phone);

            var early = await Booking(14);
            check(await Mark(early) == 51065, "Reject before threshold");
            var pending = await Booking(20, "Pending");
            check(await Mark(pending) == 51064, "Reject pending booking");
            var late = await Booking(20);
            check(Convert.ToInt32(await Sql($"SELECT COUNT(*) FROM dbo.v_NoShowAlerts WHERE Id={late};")) == 1, "Late booking appears in shared alert view");
            check(await Mark(late) == 0, "Mark no-show succeeds");
            check((string?)await Sql($"SELECT Status FROM dbo.DiningTables WHERE Id=(SELECT TableId FROM dbo.Reservations WHERE Id={late});") == "Available", "Old table released");
            check(Convert.ToInt32(await Sql($"SELECT COUNT(*) FROM dbo.v_NoShowAlerts WHERE Id={late};")) == 0, "Warning removed");
            check(Convert.ToInt32(await Sql($"SELECT COUNT(*) FROM dbo.ReservationNoShowHistory h JOIN dbo.Reservations r ON r.Id=h.ReservationId WHERE r.Id={late} AND h.NoShowAt=r.NoShowAt AND h.AppointmentAt=r.StartsAt AND h.NormalizedPhone='0912345678';")) == 1, "History timestamp and normalized phone match booking");
            check(await Mark(late) == 51064, "Repeat rejected");
            check(Convert.ToInt32(await Sql($"SELECT COUNT(*) FROM dbo.ReservationNoShowHistory WHERE ReservationId={late};")) == 1, "Repeat does not duplicate history");
            var concurrent = await Booking(20);
            var results = await Task.WhenAll(Mark(concurrent), Mark(concurrent));
            check(results.Count(x => x == 0) == 1 && results.Count(x => x == 51064) == 1, "Concurrent staff: exactly one succeeds");
            check(Convert.ToInt32(await Sql($"SELECT COUNT(*) FROM dbo.ReservationNoShowHistory WHERE ReservationId={concurrent};")) == 1, "Concurrent history unique");

            var arrived = await Booking(20);
            await Sql("INSERT dbo.Shifts(Name,BusinessDate,OpenedBy) VALUES(N'Test',CONVERT(date,SYSUTCDATETIME()),1);");
            await Sql($"DECLARE @table int=(SELECT TableId FROM dbo.Reservations WHERE Id={arrived}); EXEC dbo.usp_OpenSession @table,2,1,{arrived};");
            check(await Mark(arrived) == 51064, "Arrival committed before no-show confirmation is protected");
            check((string?)await Sql($"SELECT Status FROM dbo.DiningTables WHERE Id=(SELECT TableId FROM dbo.Reservations WHERE Id={arrived});") == "Serving", "Arrived table remains occupied");
            check(Convert.ToInt32(await Sql($"SELECT COUNT(*) FROM dbo.ReservationNoShowHistory WHERE ReservationId={arrived};")) == 0, "Failed operation creates no history");
            check(Convert.ToInt32(await Sql("SELECT COUNT(*) FROM dbo.ReservationNoShowHistory WHERE NormalizedPhone='0912345678';")) == 2, "Same phone aggregates successful visits only");
            await Sql("CREATE USER NoShowReader WITHOUT LOGIN; ALTER ROLE restaurant_app ADD MEMBER NoShowReader;");
            check(Convert.ToInt32(await Sql("EXECUTE AS USER='NoShowReader'; SELECT COUNT(*) FROM dbo.ReservationNoShowHistory WHERE NormalizedPhone=dbo.fn_NoShowPhone('+84 912345678'); REVERT;")) == 2,"Restricted application role can read normalized history");
            var rollback = await Booking(20);
            await Sql("CREATE TRIGGER dbo.TestNoShowFailure ON dbo.ReservationNoShowHistory AFTER INSERT AS THROW 51999,'Test history failure',1;");
            check(await Mark(rollback) == 51999,"History failure propagated");
            check((string?)await Sql($"SELECT Status FROM dbo.Reservations WHERE Id={rollback};") == "Confirmed", "History failure rolls back booking");
            check((string?)await Sql($"SELECT Status FROM dbo.DiningTables WHERE Id=(SELECT TableId FROM dbo.Reservations WHERE Id={rollback});") == "Reserved", "History failure preserves table hold");
            await Sql("DROP TRIGGER dbo.TestNoShowFailure;");
            // Keep the browser fixture focused on one booking becoming late.
            await Sql($"UPDATE dbo.Reservations SET Status='Cancelled' WHERE Id={rollback};");
            if(Environment.GetEnvironmentVariable("NOSHOW_BROWSER_TEST")=="1") await NoShowWebTests.Run(cs,early,check);
        }
        finally
        {
            SqlConnection.ClearAllPools(); b.InitialCatalog = "master";
            await using var c = new SqlConnection(b.ConnectionString); await c.OpenAsync();
            await using var cmd = new SqlCommand($"IF DB_ID('{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END", c);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
