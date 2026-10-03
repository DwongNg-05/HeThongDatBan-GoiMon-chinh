using System.Data;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.ViewModels;

namespace RestaurantManagement.Web.Services;

public class ReservationStore
{
	private readonly string _connectionString;

	public ReservationStore(IConfiguration configuration)
	{
		_connectionString =
			configuration.GetConnectionString("DefaultConnection")
			?? throw new InvalidOperationException(
				"Chưa cấu hình kết nối database.");
	}

	public async Task<ReservationDetailsViewModel?> LookupAsync(
		string code,
		string phone)
	{
		var normalizedPhone = ReservationPhoneNormalizer.Normalize(phone);
		var normalizedCode = code.Trim().ToUpperInvariant();

		// Không để SQL tự cắt mã quá dài thành một mã hợp lệ.
		if (normalizedPhone is null || normalizedCode.Length != 6)
			return null;

		await using var connection = new SqlConnection(_connectionString);
		await connection.OpenAsync();

		const string sql = """
            SELECT
                r.Code,
                r.Status,
                r.StartsAt,
                r.GuestCount,
                COALESCE(preferredArea.Name, tableArea.Name) AS AreaName,
                t.Code AS TableCode,
                settings.Phone AS RestaurantPhone,
                settings.CancelCutoffMinutes,
                SYSUTCDATETIME() AS ServerNow
            FROM dbo.Reservations r
            LEFT JOIN dbo.Areas preferredArea
                ON preferredArea.Id = r.PreferredAreaId
            LEFT JOIN dbo.DiningTables t
                ON t.Id = r.TableId
            LEFT JOIN dbo.Areas tableArea
                ON tableArea.Id = t.AreaId
            CROSS JOIN dbo.RestaurantSettings settings
            WHERE settings.Id = 1
                AND r.Code = @Code
                AND r.Phone = @Phone;
            """;

		await using var command = new SqlCommand(sql, connection);

		command.Parameters.Add("@Code", SqlDbType.Char, 6)
			.Value = normalizedCode;

		command.Parameters.Add("@Phone", SqlDbType.VarChar, 10)
			.Value = normalizedPhone;

		await using var reader = await command.ExecuteReaderAsync();

		if (!await reader.ReadAsync())
			return null;

		var startsAt = DateTime.SpecifyKind(
			reader.GetDateTime(2),
			DateTimeKind.Utc);

		var serverNow = DateTime.SpecifyKind(
			reader.GetDateTime(8),
			DateTimeKind.Utc);

		var cutoffMinutes = reader.GetInt32(7);

		var result = new ReservationDetailsViewModel
		{
			Code = reader.GetString(0).Trim(),
			Status = reader.GetString(1),
			StartsAtUtc = new DateTimeOffset(startsAt),
			GuestCount = reader.GetInt32(3),
			AreaName = reader.IsDBNull(4) ? null : reader.GetString(4),
			TableCode = reader.IsDBNull(5) ? null : reader.GetString(5),
			RestaurantPhone = reader.GetString(6)
		};

		var allowedStatus =
			result.Status is "Pending" or "Confirmed";

		var enoughTime =
			startsAt >= serverNow.AddMinutes(cutoffMinutes);

		result.CanCancel = allowedStatus && enoughTime;

		result.CancellationMessage = result.Status switch
		{
			"Cancelled" => "Đặt bàn này đã được huỷ.",
			"Arrived" =>
				"Bạn đã đến quán. Vui lòng liên hệ nhân viên để được hỗ trợ.",
			"NoShow" =>
				"Đặt bàn này đã được ghi nhận vắng mặt.",
			"Rejected" =>
				"Đặt bàn này đã bị từ chối.",
			"Pending" or "Confirmed" when !enoughTime =>
				$"Đặt bàn còn dưới {cutoffMinutes} phút đến giờ hẹn. " +
				"Vui lòng gọi trực tiếp cho quán để được hỗ trợ.",
			"Pending" or "Confirmed" => null,
			_ => "Trạng thái hiện tại không cho phép huỷ."
		};

		return result;
	}

	public async Task CancelAsync(string code, string phone)
	{
		var normalizedPhone = ReservationPhoneNormalizer.Normalize(phone);
		var normalizedCode = code.Trim().ToUpperInvariant();

		if (normalizedPhone is null || normalizedCode.Length != 6)
		{
			throw new ArgumentException(
				"Mã đặt bàn hoặc số điện thoại không hợp lệ.");
		}

		await using var connection = new SqlConnection(_connectionString);
		await connection.OpenAsync();

		await using var command = new SqlCommand(
			"dbo.usp_CancelReservation",
			connection)
		{
			CommandType = CommandType.StoredProcedure
		};

		command.Parameters.Add("@Code", SqlDbType.Char, 6)
			.Value = normalizedCode;

		command.Parameters.Add("@Phone", SqlDbType.VarChar, 10)
			.Value = normalizedPhone;

		command.Parameters.Add("@Reason", SqlDbType.NVarChar, 500)
			.Value = "Khách huỷ trực tuyến";

		await command.ExecuteNonQueryAsync();
	}
}