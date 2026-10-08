using System.Data;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.ViewModels;

namespace RestaurantManagement.Web.Services;

public sealed class LookupRateLimitStatus
{
	public bool IsBlocked { get; set; }

	public DateTimeOffset? BlockedUntilUtc { get; set; }

	public int RemainingSeconds { get; set; }
}

public sealed class LookupAttemptResult
{
	public int FailureCount { get; set; }

	public bool IsBlocked { get; set; }

	public DateTimeOffset? BlockedUntilUtc { get; set; }

	public int RemainingSeconds { get; set; }
}

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
		var normalizedPhone =
			ReservationPhoneNormalizer.Normalize(phone);

		var normalizedCode =
			code.Trim().ToUpperInvariant();

		if (normalizedPhone is null ||
			normalizedCode.Length != 6)
		{
			return null;
		}

		await using var connection =
			new SqlConnection(_connectionString);

		await connection.OpenAsync();

		const string sql = """
            SELECT
                r.Code,
                r.Status,
                r.StartsAt,
                r.GuestCount,
                COALESCE(
                    preferredArea.Name,
                    tableArea.Name
                ) AS AreaName,
                t.Code AS TableCode,
                settings.Phone AS RestaurantPhone,
                settings.CancelCutoffMinutes,
                SYSUTCDATETIME() AS ServerNow,
                r.Email,
                r.RejectionReason
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

		await using var command =
			new SqlCommand(sql, connection);

		command.Parameters
			.Add("@Code", SqlDbType.Char, 6)
			.Value = normalizedCode;

		command.Parameters
			.Add("@Phone", SqlDbType.VarChar, 10)
			.Value = normalizedPhone;

		await using var reader =
			await command.ExecuteReaderAsync();

		if (!await reader.ReadAsync())
			return null;

		var startsAt = DateTime.SpecifyKind(
			reader.GetDateTime(2),
			DateTimeKind.Utc);

		var serverNow = DateTime.SpecifyKind(
			reader.GetDateTime(8),
			DateTimeKind.Utc);

		var cutoffMinutes =
			reader.GetInt32(7);

		var result = new ReservationDetailsViewModel
		{
			Code = reader.GetString(0).Trim(),

			Status = reader.GetString(1),

			StartsAtUtc =
				new DateTimeOffset(startsAt),

			GuestCount =
				reader.GetInt32(3),

			AreaName =
				reader.IsDBNull(4)
					? null
					: reader.GetString(4),

			TableCode =
				reader.IsDBNull(5)
					? null
					: reader.GetString(5),

			RestaurantPhone =
				reader.GetString(6),

			Email =
				reader.IsDBNull(9)
					? null
					: reader.GetString(9),

			RejectionReason =
				reader.IsDBNull(10)
					? null
					: reader.GetString(10)
		};

		var allowedStatus =
			result.Status is "Pending" or "Confirmed";

		var enoughTime =
			startsAt >=
			serverNow.AddMinutes(cutoffMinutes);

		result.CanCancel =
			allowedStatus && enoughTime;

		result.CancellationMessage =
			result.Status switch
			{
				"Cancelled" =>
					"Đặt bàn này đã được huỷ.",

				"Arrived" =>
					"Bạn đã đến quán. " +
					"Vui lòng liên hệ nhân viên để được hỗ trợ.",

				"NoShow" =>
					"Đặt bàn này đã được ghi nhận vắng mặt.",

				"Rejected" =>
					result.RejectionMessage ?? "Đặt bàn này đã bị từ chối.",

				"Pending" or "Confirmed"
					when !enoughTime =>
					$"Đặt bàn còn dưới {cutoffMinutes} phút " +
					"đến giờ hẹn. Vui lòng gọi trực tiếp " +
					"cho quán để được hỗ trợ.",

				"Pending" or "Confirmed" =>
					null,

				_ =>
					"Trạng thái hiện tại không cho phép huỷ."
			};

		return result;
	}

	public async Task CancelAsync(
		string code,
		string phone)
	{
		var normalizedPhone =
			ReservationPhoneNormalizer.Normalize(phone);

		var normalizedCode =
			code.Trim().ToUpperInvariant();

		if (normalizedPhone is null ||
			normalizedCode.Length != 6)
		{
			throw new ArgumentException(
				"Mã đặt bàn hoặc số điện thoại không hợp lệ.");
		}

		await using var connection =
			new SqlConnection(_connectionString);

		await connection.OpenAsync();

		await using var command =
			new SqlCommand(
				"dbo.usp_CancelReservation",
				connection)
			{
				CommandType =
					CommandType.StoredProcedure
			};

		command.Parameters
			.Add("@Code", SqlDbType.Char, 6)
			.Value = normalizedCode;

		command.Parameters
			.Add("@Phone", SqlDbType.VarChar, 10)
			.Value = normalizedPhone;

		command.Parameters
			.Add(
				"@Reason",
				SqlDbType.NVarChar,
				500)
			.Value =
				"Khách huỷ trực tuyến";

		await command.ExecuteNonQueryAsync();
	}

	public async Task<string?> GetCancellationEmailStatusAsync(
		string code,
		string phone)
	{
		var normalizedPhone =
			ReservationPhoneNormalizer.Normalize(phone);

		var normalizedCode =
			code.Trim().ToUpperInvariant();

		if (normalizedPhone is null ||
			normalizedCode.Length != 6)
		{
			return null;
		}

		await using var connection =
			new SqlConnection(_connectionString);

		await connection.OpenAsync();

		const string sql = """
            SELECT TOP (1)
                o.Status
            FROM dbo.EmailOutbox o
            INNER JOIN dbo.Reservations r
                ON r.Id = o.ReservationId
            WHERE r.Code = @Code
                AND r.Phone = @Phone
                AND o.MessageType = 'BookingCancelled'
            ORDER BY o.Id DESC;
            """;

		await using var command =
			new SqlCommand(sql, connection);

		command.Parameters
			.Add("@Code", SqlDbType.Char, 6)
			.Value = normalizedCode;

		command.Parameters
			.Add("@Phone", SqlDbType.VarChar, 10)
			.Value = normalizedPhone;

		var value =
			await command.ExecuteScalarAsync();

		return value is null ||
			   value == DBNull.Value
			? null
			: Convert.ToString(value);
	}

	public async Task<LookupRateLimitStatus>
		GetLookupRateLimitStatusAsync(
			string ipAddress)
	{
		await using var connection =
			new SqlConnection(_connectionString);

		await connection.OpenAsync();

		await using var command =
			new SqlCommand(
				"dbo.usp_GetReservationLookupRateLimit",
				connection)
			{
				CommandType =
					CommandType.StoredProcedure
			};

		command.Parameters
			.Add(
				"@IpAddress",
				SqlDbType.VarChar,
				45)
			.Value = ipAddress;

		await using var reader =
			await command.ExecuteReaderAsync();

		if (!await reader.ReadAsync())
		{
			return new LookupRateLimitStatus();
		}

		var result =
			new LookupRateLimitStatus
			{
				IsBlocked =
					reader.GetBoolean(0),

				RemainingSeconds =
					reader.IsDBNull(2)
						? 0
						: reader.GetInt32(2)
			};

		if (!reader.IsDBNull(1))
		{
			var blockedUntil =
				DateTime.SpecifyKind(
					reader.GetDateTime(1),
					DateTimeKind.Utc);

			result.BlockedUntilUtc =
				new DateTimeOffset(
					blockedUntil);
		}

		return result;
	}

	public async Task<LookupAttemptResult>
		RecordLookupAttemptAsync(
			string ipAddress,
			bool succeeded)
	{
		await using var connection =
			new SqlConnection(_connectionString);

		await connection.OpenAsync();

		await using var command =
			new SqlCommand(
				"dbo.usp_RecordReservationLookupAttempt",
				connection)
			{
				CommandType =
					CommandType.StoredProcedure
			};

		command.Parameters
			.Add(
				"@IpAddress",
				SqlDbType.VarChar,
				45)
			.Value = ipAddress;

		command.Parameters
			.Add(
				"@Succeeded",
				SqlDbType.Bit)
			.Value = succeeded;

		await using var reader =
			await command.ExecuteReaderAsync();

		if (!await reader.ReadAsync())
		{
			return new LookupAttemptResult();
		}

		var result =
			new LookupAttemptResult
			{
				FailureCount =
					reader.IsDBNull(0)
						? 0
						: reader.GetInt32(0),

				IsBlocked =
					reader.GetBoolean(1),

				RemainingSeconds =
					reader.IsDBNull(3)
						? 0
						: reader.GetInt32(3)
			};

		if (!reader.IsDBNull(2))
		{
			var blockedUntil =
				DateTime.SpecifyKind(
					reader.GetDateTime(2),
					DateTimeKind.Utc);

			result.BlockedUntilUtc =
				new DateTimeOffset(
					blockedUntil);
		}

		return result;
	}

	public async Task<string>
		GetRestaurantPhoneAsync()
	{
		await using var connection =
			new SqlConnection(_connectionString);

		await connection.OpenAsync();

		const string sql = """
            SELECT Phone
            FROM dbo.RestaurantSettings
            WHERE Id = 1;
            """;

		await using var command =
			new SqlCommand(sql, connection);

		var value =
			await command.ExecuteScalarAsync();

		return value is null ||
			   value == DBNull.Value
			? ""
			: Convert.ToString(value) ?? "";
	}
}
