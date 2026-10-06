using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Reservations;

internal static class DailyReservationTests
{
    internal static void Run(Action<bool, string> check)
    {
        var date = new DateOnly(2026, 10, 2);
        check(DailyReservationRules.Today(new DateTime(2026, 10, 1, 17, 0, 0)) == date, "S2-05: Vietnam midnight selects new day");
        check(DailyReservationRules.Today(new DateTime(2026, 10, 1, 16, 59, 59)) == date.AddDays(-1), "S2-05: instant before Vietnam midnight selects previous day");
        var bounds = DailyReservationRules.UtcBounds(date);
        check(bounds.Start == new DateTime(2026, 10, 1, 17, 0, 0) && bounds.End == new DateTime(2026, 10, 2, 17, 0, 0), "S2-05: exclusive UTC day bounds");
        check(DailyReservationRules.MaskPhone("0901234123") == "090****123", "S2-05: exactly four middle digits masked");
        check(DailyReservationRules.MaskPhone("0000000099") == "000****099", "S2-05: supported ten-digit demo format masked");
        check(new[] { "", "+84901234123", "090 123 4123", "090abc4123", "12345678901" }.All(p => DailyReservationRules.MaskPhone(p) == "**********"), "S2-05: unsupported phone formats never disclosed");
        check(DailyReservationRules.TimeSlot(bounds.Start.AddHours(8), bounds.Start.AddHours(9.5)) == "08:00–09:30", "S2-05: Vietnam time slot displayed");
        check(DailyReservationRules.TimeSlot(bounds.End.AddMinutes(-30), bounds.End.AddHours(1)) == "23:30–01:00 (03/10/2026)", "S2-05: overnight slot includes end date");
        check(DailyReservationRules.StatusLabel("NoShow") == "Khách không tới", "S2-05: status label translated");
        check(DailyReservationRules.IsSupportedFilter(null) && new[] { "", "Pending", "Confirmed", "Cancelled", "NoShow" }.All(DailyReservationRules.IsSupportedFilter), "S2-05 Task 2: supported filters and default all");
        check(new[] { "Unknown", "pending", "Pending,Confirmed", "Rejected", "Arrived" }.All(s => !DailyReservationRules.IsSupportedFilter(s)), "S2-05 Task 2: invalid single-status filters rejected");
        var now = new DateTime(2026, 10, 2, 1, 0, 0, DateTimeKind.Utc);
        foreach (var status in new[] { "Pending", "Confirmed", "Cancelled", "NoShow", "Rejected", "Arrived" })
        {
            var eligible = status is "Pending" or "Confirmed";
            foreach (var (seconds, inside) in new[] { (-1, false), (0, true), (1, true), (1799, true), (1800, true), (1801, false) })
                check(DailyReservationRules.IsUpcoming(now.AddSeconds(seconds), status, now) == (eligible && inside), $"S2-05 Task 3: boundary {seconds}s for {status}");
        }
    }

    internal static async Task Sql(Action<bool, string> check)
    {
        var database = "S205Test_" + Guid.NewGuid().ToString("N");
        var builder = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("RM_CONNECTION_STRING")
            ?? "Server=(localdb)\\MSSQLLocalDB;Integrated Security=True;TrustServerCertificate=True");
        builder.InitialCatalog = "master";
        await using var master = new SqlConnection(builder.ConnectionString);
        await master.OpenAsync();
        await new SqlCommand($"CREATE DATABASE [{database}]", master).ExecuteNonQueryAsync();
        try
        {
            builder.InitialCatalog = database;
            await using (var cn = new SqlConnection(builder.ConnectionString))
            {
                await cn.OpenAsync();
                await new SqlCommand("""
                    CREATE TABLE dbo.DiningTables(Id int PRIMARY KEY,Code varchar(20));
                    CREATE TABLE dbo.Reservations(Id bigint,Code varchar(6),CustomerName nvarchar(100),Phone varchar(10),GuestCount int,
                        StartsAt datetime2,EndsAt datetime2,TableId int NULL,Status varchar(20));
                    INSERT dbo.DiningTables VALUES(1,'A01');
                    INSERT dbo.Reservations VALUES
                    (2,'LATE02',N'Khách chiều','0901234123',4,'2026-10-02T08:00:00','2026-10-02T09:30:00',1,'Confirmed'),
                    (3,'EARLY3',N'Khách sáng','0912345123',2,'2026-10-02T01:00:00','2026-10-02T02:30:00',NULL,'Pending'),
                    (1,'EARLY1',N'Khách cùng giờ','0923456123',3,'2026-10-02T01:00:00','2026-10-02T02:30:00',NULL,'Cancelled'),
                    (4,'BEFORE',N'Ngày trước','0934567123',2,'2026-10-01T16:59:59','2026-10-01T18:00:00',NULL,'Pending'),
                    (5,'AFTER5',N'Ngày sau','0945678123',2,'2026-10-02T17:00:00','2026-10-02T18:30:00',NULL,'Pending');
                    """, cn).ExecuteNonQueryAsync();
            }
            var store = new DailyReservationStore(builder.ConnectionString);
            var page = await store.Read(new DateOnly(2026, 10, 2), default);
            check(page.Reservations.Select(r => r.Code).SequenceEqual(new[] { "EARLY1", "EARLY3", "LATE02" }), "S2-05 SQL: only selected Vietnam day, ascending appointment and stable ties");
            check(page.Reservations[2] is { CustomerName: "Khách chiều", Phone: "090****123", GuestCount: 4, TimeSlot: "15:00–16:30", TableName: "A01", Status: "Đã xác nhận" }, "S2-05 SQL: all returned fields and masked phone");
            check(page.Reservations[0].TableName == "Chưa xếp bàn", "S2-05 SQL: unassigned reservation");
            var json = System.Text.Json.JsonSerializer.Serialize(page);
            check(!json.Contains("0901234123") && !json.Contains("0912345123"), "S2-05 SQL: serialized response excludes raw phones");
            check((await store.Read(new DateOnly(2026, 10, 4), default)).Reservations.Count == 0, "S2-05 SQL: empty selected day");
            await using (var cn = new SqlConnection(builder.ConnectionString))
            {
                await cn.OpenAsync();
                await new SqlCommand("""
                    INSERT dbo.Reservations VALUES
                    (6,'NOSHOW',N'Khách không tới','0956789123',2,'2026-10-02T03:00:00','2026-10-02T04:30:00',NULL,'NoShow'),
                    (7,'PEND07',N'Khách chờ','0967890123',5,'2026-10-02T09:00:00','2026-10-02T10:30:00',NULL,'Pending'),
                    (8,'REJECT',N'Khách từ chối','0978901123',2,'2026-10-02T05:00:00','2026-10-02T06:30:00',NULL,'Rejected'),
                    (9,'ARRIVE',N'Khách tới','0989012123',2,'2026-10-02T06:00:00','2026-10-02T07:30:00',1,'Arrived');
                    """, cn).ExecuteNonQueryAsync();
            }
            foreach (var (status, codes) in new[] {
                ("Pending", new[] { "EARLY3", "PEND07" }), ("Confirmed", new[] { "LATE02" }),
                ("Cancelled", new[] { "EARLY1" }), ("NoShow", new[] { "NOSHOW" }) })
            {
                var filtered = await store.Read(new DateOnly(2026, 10, 2), default, status);
                check(filtered.Reservations.Select(r => r.Code).SequenceEqual(codes), "S2-05 Task 2 SQL: " + status + " filter retains day and ascending order");
                check(filtered.Reservations.All(r => r.Phone.Contains("****") && r.CustomerName.Length > 0 && r.TimeSlot.Length > 0 && r.TableName.Length > 0 && r.GuestCount > 0), "S2-05 Task 2 SQL: filtered fields preserved for " + status);
            }
            check((await store.Read(new DateOnly(2026, 10, 3), default, "Confirmed")).Reservations.Count == 0, "S2-05 Task 2 SQL: empty filter result");
            check((await store.Read(new DateOnly(2026, 10, 2), default, "")).Reservations.Count == 7, "S2-05 Task 2 SQL: clear restores all statuses including rejected and arrived");
            await using (var cn = new SqlConnection(builder.ConnectionString))
            {
                await cn.OpenAsync();
                await new SqlCommand("""
                    INSERT dbo.Reservations VALUES
                    (10,'SOON10',N'Sắp đến 1','0900000010',2,'2026-10-02T08:15:00','2026-10-02T09:45:00',NULL,'Pending'),
                    (11,'SOON11',N'Sắp đến 2','0900000011',2,'2026-10-02T08:30:00','2026-10-02T10:00:00',1,'Confirmed'),
                    (12,'NIGHT1',N'Cuối ngày','0900000012',2,'2026-10-02T16:55:00','2026-10-02T18:25:00',NULL,'Pending');
                    """, cn).ExecuteNonQueryAsync();
            }
            var testNow = new DateTime(2026, 10, 2, 8, 0, 0, DateTimeKind.Utc);
            var grouped = await store.Read(new DateOnly(2026, 10, 2), default, null, testNow);
            check(grouped.Reservations.Select(r => r.Code).SequenceEqual(new[] { "LATE02", "SOON10", "SOON11", "EARLY1", "EARLY3", "NOSHOW", "REJECT", "ARRIVE", "PEND07", "NIGHT1" }), "S2-05 Task 3 SQL: upcoming first and both groups ordered");
            check(grouped.Reservations.Count(r => r.IsUpcoming) == 3 && grouped.Reservations.Select(r => r.Code).Distinct().Count() == 10, "S2-05 Task 3 SQL: no duplicates between groups");
            var filteredUpcoming = await store.Read(new DateOnly(2026, 10, 2), default, "Pending", testNow);
            check(filteredUpcoming.Reservations.Select(r => r.Code).SequenceEqual(new[] { "SOON10", "EARLY3", "PEND07", "NIGHT1" }), "S2-05 Task 3 SQL: grouping applies to active filter");
            var later = await store.Read(new DateOnly(2026, 10, 2), default, null, testNow.AddMinutes(31));
            check(!later.Reservations.Any(r => r.Code is "LATE02" or "SOON10" or "SOON11" && r.IsUpcoming) && later.Reservations.Single(r => r.Code == "PEND07").IsUpcoming, "S2-05 Task 3 SQL: time changes upcoming membership");
            var none = await store.Read(new DateOnly(2026, 10, 2), default, "Cancelled", testNow);
            check(none.Reservations.Count == 1 && !none.Reservations[0].IsUpcoming, "S2-05 Task 3 SQL: no upcoming group for cancelled filter");
            var night = await store.Read(new DateOnly(2026, 10, 2), default, null, new DateTime(2026, 10, 2, 16, 45, 0));
            check(night.Reservations.Where(r => r.IsUpcoming).Select(r => r.Code).SequenceEqual(new[] { "NIGHT1" }) && !night.Reservations.Any(r => r.Code == "AFTER5"), "S2-05 Task 3 SQL: midnight window excludes next-day reservation");
            var midnight = new DateTime(2026, 10, 2, 17, 0, 0, DateTimeKind.Utc);
            var nextDay = await store.Read(DailyReservationRules.Today(midnight), default, "Pending", midnight);
            check(nextDay.Reservations.Count == 1 && nextDay.Reservations[0] is { Code: "AFTER5", IsUpcoming: true }, "S2-05 Task 3 SQL: new Vietnam day includes new-day arrival at midnight");
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await new SqlCommand($"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]", master).ExecuteNonQueryAsync();
        }
    }
}
