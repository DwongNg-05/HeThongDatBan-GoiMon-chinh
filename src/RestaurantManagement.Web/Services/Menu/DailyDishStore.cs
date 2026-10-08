using System.Data;
using Microsoft.Data.SqlClient;

namespace RestaurantManagement.Web.Services;

/// <summary>
/// S2-08 Task 1: một món trên danh sách món trong ngày (/Kitchen/Dishes).
/// <paramref name="IsTemporarilyOut"/>: Bếp/Quản lý bật "Tạm hết" (MenuItems.IsTemporarilyOut).
/// <paramref name="SoldOutToday"/>: Quản lý đã "Báo hết" trong ngày ở Quản lý món (S2-01 Task 3).
/// </summary>
public sealed record DailyDish(int Id, string CategoryName, string Name, int PriceVnd, string Unit, bool IsTemporarilyOut, bool SoldOutToday)
{
    /// <summary>Món không nhận order mới: đang tạm hết hoặc đã báo hết trong ngày.</summary>
    public bool IsUnavailable => IsTemporarilyOut || SoldOutToday;
}

/// <summary>S2-08 Task 1: trạng thái nhận order của các món đang bán (chỉ liệt kê món đang không nhận order).</summary>
public sealed record DishAvailability(IReadOnlyList<int> TemporarilyOut, IReadOnlyList<int> SoldOutToday)
{
    /// <summary>Món không nhận order mới: tạm hết hoặc hết trong ngày, tăng dần, không trùng.</summary>
    public IReadOnlyList<int> Unavailable => TemporarilyOut.Union(SoldOutToday).Order().ToArray();

    public bool IsUnavailable(int dishId) => TemporarilyOut.Contains(dishId) || SoldOutToday.Contains(dishId);
}

/// <summary>
/// S2-08 Task 1 – quy tắc đã chốt: Bếp và Quản lý bật/tắt "Tạm hết" (một chạm, trên danh sách món trong ngày);
/// thực đơn công khai và màn hình gọi món hiển thị thay đổi trong tối đa 5 giây.
/// </summary>
public static class TemporaryOutRules
{
    /// <summary>Vai trò được bật/tắt "Tạm hết" (khớp dbo.Roles.Code).</summary>
    public const string AllowedRoles = RestaurantManagement.Web.Security.AppRoles.Manager + "," + RestaurantManagement.Web.Security.AppRoles.Kitchen;

    /// <summary>Chu kỳ các trang hỏi lại trạng thái món (menu-availability.js).</summary>
    public const int PollIntervalMs = 3000;

    /// <summary>Độ trễ hiển thị tối đa theo tiêu chí nghiệm thu.</summary>
    public const int MaxDisplayDelayMs = 5000;

    public const string Label = "Tạm hết";
}

/// <summary>
/// S2-08 Task 2: một dòng nhật ký bật/tắt "Tạm hết" (dbo.MenuTemporaryOutEvents).
/// <paramref name="ChangedById"/> null = hệ thống tự đặt lại lúc 00:00 Asia/Ho_Chi_Minh (S2-08 Task 3, đã chốt với PO: vẫn ghi nhật ký).
/// </summary>
public sealed record TemporaryOutLogEntry(long Id, int DishId, string DishName, bool? OldIsTemporarilyOut, bool IsTemporarilyOut,
    int? ChangedById, string? ChangedByFullName, string? ChangedByUserName, string? ChangedByRole, DateTime ChangedAtUtc)
{
    public static string StateLabel(bool? isTemporarilyOut) => isTemporarilyOut switch
    {
        true => "Tạm hết",
        false => "Còn món",
        null => "—"
    };

    public string OldStateLabel => StateLabel(OldIsTemporarilyOut);
    public string NewStateLabel => StateLabel(IsTemporarilyOut);
    public const string SystemActor = "Hệ thống – tự đặt lại 00:00";

    public bool IsAutomaticReset => ChangedById is null;
    public string ActionLabel => IsTemporarilyOut ? "Bật tạm hết" : IsAutomaticReset ? "Tự đặt lại lúc 00:00" : "Tắt tạm hết";

    /// <summary>Người thực hiện: "Họ tên (tên đăng nhập) · Vai trò", hoặc hệ thống (tự đặt lại lúc 00:00).</summary>
    public string ActorLabel => ChangedById is null
        ? SystemActor
        : $"{ChangedByFullName} ({ChangedByUserName})" + (string.IsNullOrEmpty(ChangedByRole) ? "" : $" · {ChangedByRole}");
}

public enum TemporaryOutStatus { Updated, Forbidden, NotFound }

public sealed record TemporaryOutResult(TemporaryOutStatus Status, string? DishName = null, bool IsTemporarilyOut = false);

/// <summary>
/// S2-08 Task 1: đọc danh sách món trong ngày, bật/tắt "Tạm hết" và trả danh sách món đang không nhận order
/// (dùng cho thực đơn công khai và màn hình gọi món tự cập nhật trong tối đa 5 giây).
/// </summary>
public sealed class DailyDishStore(string connectionString)
{
    /// <summary>Điều kiện "hết trong ngày" giống dbo.vw_PublicMenu: chỉ đúng trong ngày nghiệp vụ UTC+7 hiện tại.</summary>
    private const string SoldOutTodaySql =
        "CONVERT(bit,CASE WHEN m.IsSoldOut=1 AND m.SoldOutBusinessDate=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME())) THEN 1 ELSE 0 END)";

    /// <summary>Món đang bán thuộc nhóm đang dùng, theo thứ tự nhóm rồi thứ tự món.</summary>
    public async Task<IReadOnlyList<DailyDish>> List(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await using var command = new SqlCommand($"""
            SELECT m.Id,c.Name,m.Name,CAST(m.Price AS int),m.Unit,m.IsTemporarilyOut,{SoldOutTodaySql}
            FROM dbo.MenuItems m JOIN dbo.MenuCategories c ON c.Id=m.CategoryId
            WHERE m.IsActive=1 AND c.IsActive=1
            ORDER BY c.SortOrder,c.Id,m.SortOrder,m.Name,m.Id;
            """, connection);
        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var dishes = new List<DailyDish>();
        while (await reader.ReadAsync(cancellationToken))
            dishes.Add(new DailyDish(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3),
                reader.GetString(4), reader.GetBoolean(5), reader.GetBoolean(6)));
        return dishes;
    }

    private const string AvailabilitySql = $"""
        SELECT m.Id,m.IsTemporarilyOut,{SoldOutTodaySql} FROM dbo.MenuItems m
        WHERE m.IsActive=1 AND (m.IsTemporarilyOut=1 OR {SoldOutTodaySql}=1)
        ORDER BY m.Id;
        """;

    /// <summary>Các món đang bán nhưng hiện không nhận order (tạm hết hoặc hết trong ngày). Truy vấn nhẹ, gọi mỗi 3 giây.</summary>
    public async Task<DishAvailability> Availability(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await using var command = new SqlCommand(AvailabilitySql, connection);
        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        List<int> temporarilyOut = [], soldOutToday = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.GetBoolean(1)) temporarilyOut.Add(reader.GetInt32(0));
            if (reader.GetBoolean(2)) soldOutToday.Add(reader.GetInt32(0));
        }
        return new DishAvailability(temporarilyOut, soldOutToday);
    }

    /// <summary>Bản đồng bộ của <see cref="Availability"/> cho các handler Razor Page đồng bộ (OnGet/OnPost).</summary>
    public DishAvailability AvailabilityNow()
    {
        using var connection = new SqlConnection(connectionString);
        using var command = new SqlCommand(AvailabilitySql, connection);
        connection.Open();
        using var reader = command.ExecuteReader();
        List<int> temporarilyOut = [], soldOutToday = [];
        while (reader.Read())
        {
            if (reader.GetBoolean(1)) temporarilyOut.Add(reader.GetInt32(0));
            if (reader.GetBoolean(2)) soldOutToday.Add(reader.GetInt32(0));
        }
        return new DishAvailability(temporarilyOut, soldOutToday);
    }

    /// <summary>Bật/tắt "Tạm hết" qua dbo.usp_SetMenuTemporarilyOut (kiểm tra quyền Menu.TemporarilyOut, ghi lịch sử).</summary>
    /// <remarks>
    /// Quyền được kiểm tra theo vai trò của tài khoản trong database (Bếp hoặc Quản lý, đang hoạt động) ngay trong cùng giao dịch,
    /// nên Bếp bật/tắt được kể cả khi database chưa có quyền Menu.TemporarilyOut (migration 038) — không phụ thuộc bảng RolePermissions.
    /// Món phải đang bán và thuộc nhóm đang dùng. Mỗi lần đổi trạng thái được ghi vào dbo.MenuTemporaryOutEvents (migration 006).
    /// </remarks>
    public async Task<TemporaryOutResult> SetTemporarilyOut(int dishId, bool isTemporarilyOut, int actorUserId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await using var command = new SqlCommand(SetTemporarilyOutSql, connection);
        command.Parameters.Add("@MenuItemId", SqlDbType.Int).Value = dishId;
        command.Parameters.Add("@IsTemporarilyOut", SqlDbType.Bit).Value = isTemporarilyOut;
        command.Parameters.Add("@ActorUserId", SqlDbType.Int).Value = actorUserId;
        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return new(TemporaryOutStatus.NotFound);
        return reader.GetInt32(0) switch
        {
            0 => new TemporaryOutResult(TemporaryOutStatus.Updated, reader.GetString(1), reader.GetBoolean(2)),
            1 => new TemporaryOutResult(TemporaryOutStatus.Forbidden),
            _ => new TemporaryOutResult(TemporaryOutStatus.NotFound)
        };
    }

    /// <summary>
    /// S2-08 Task 2: lịch sử bật/tắt "Tạm hết", mới nhất trước (cùng thời điểm thì dòng ghi sau đứng trước).
    /// <paramref name="dishId"/> null = mọi món.
    /// </summary>
    public async Task<IReadOnlyList<TemporaryOutLogEntry>> History(int? dishId, int take = 200, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await using var command = new SqlCommand("""
            SELECT TOP(@Take) e.Id,e.MenuItemId,m.Name,e.OldIsTemporarilyOut,e.IsTemporarilyOut,e.ChangedBy,
                   u.FullName,u.UserName,r.Name,e.ChangedAt
            FROM dbo.MenuTemporaryOutEvents e
            JOIN dbo.MenuItems m ON m.Id=e.MenuItemId
            LEFT JOIN dbo.Users u ON u.Id=e.ChangedBy
            LEFT JOIN dbo.Roles r ON r.Id=u.RoleId
            WHERE @MenuItemId IS NULL OR e.MenuItemId=@MenuItemId
            ORDER BY e.ChangedAt DESC, e.Id DESC;
            """, connection);
        command.Parameters.Add("@Take", SqlDbType.Int).Value = Math.Clamp(take, 1, 1000);
        command.Parameters.Add("@MenuItemId", SqlDbType.Int).Value = (object?)dishId ?? DBNull.Value;
        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<TemporaryOutLogEntry>();
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new TemporaryOutLogEntry(
                reader.GetInt64(0), reader.GetInt32(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetBoolean(3), reader.GetBoolean(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                DateTime.SpecifyKind(reader.GetDateTime(9), DateTimeKind.Utc)));
        return rows;
    }

    // Kết quả: Status 0 = đã cập nhật (kèm tên món, trạng thái mới), 1 = tài khoản không phải Bếp/Quản lý đang hoạt động, 2 = không tìm thấy món đang bán.
    private const string SetTemporarilyOutSql = """
        SET NOCOUNT ON;
        SET XACT_ABORT ON;
        BEGIN TRANSACTION;
        IF NOT EXISTS(SELECT 1 FROM dbo.Users u JOIN dbo.Roles r ON r.Id=u.RoleId
                      WHERE u.Id=@ActorUserId AND u.IsActive=1 AND r.Code IN ('Manager','Kitchen'))
        BEGIN
            ROLLBACK;
            SELECT 1 AS Status, CAST(NULL AS nvarchar(150)) AS Name, CAST(0 AS bit) AS IsTemporarilyOut;
            RETURN;
        END;
        DECLARE @old bit, @name nvarchar(150);
        SELECT @old=m.IsTemporarilyOut, @name=m.Name
        FROM dbo.MenuItems m WITH (UPDLOCK, HOLDLOCK) JOIN dbo.MenuCategories c ON c.Id=m.CategoryId
        WHERE m.Id=@MenuItemId AND m.IsActive=1 AND c.IsActive=1;
        IF @name IS NULL
        BEGIN
            ROLLBACK;
            SELECT 2 AS Status, CAST(NULL AS nvarchar(150)) AS Name, CAST(0 AS bit) AS IsTemporarilyOut;
            RETURN;
        END;
        -- S2-08 Task 2: mỗi lần trạng thái thật sự đổi ghi đúng một dòng nhật ký, trong cùng giao dịch với thay đổi
        -- (không đổi thì không ghi): người thực hiện, món, trạng thái trước, trạng thái sau, thời điểm (UTC).
        IF @old<>@IsTemporarilyOut
        BEGIN
            DECLARE @now datetime2(3)=SYSUTCDATETIME();
            UPDATE dbo.MenuItems SET IsTemporarilyOut=@IsTemporarilyOut, UpdatedAt=@now WHERE Id=@MenuItemId;
            INSERT dbo.MenuTemporaryOutEvents(MenuItemId,OldIsTemporarilyOut,IsTemporarilyOut,ChangedBy,ChangedAt)
            VALUES(@MenuItemId,@old,@IsTemporarilyOut,@ActorUserId,@now);
        END;
        COMMIT;
        SELECT 0 AS Status, @name AS Name, @IsTemporarilyOut AS IsTemporarilyOut;
        """;
}
