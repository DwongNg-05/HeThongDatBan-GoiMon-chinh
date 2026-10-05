namespace RestaurantManagement.Web.ViewModels;

public class ReservationDetailsViewModel
{
	public string Code { get; set; } = "";
	public string Status { get; set; } = "";

	public DateTimeOffset StartsAtUtc { get; set; }

	public int GuestCount { get; set; }

	public string? AreaName { get; set; }

	public string? TableCode { get; set; }

	public string RestaurantPhone { get; set; } = "";

	// Email của khách đặt bàn.
	public string? Email { get; set; }

	// Thông báo kết quả gửi email sau khi huỷ.
	public string? EmailDeliveryMessage { get; set; }

	public bool CanCancel { get; set; }

	public string? CancellationMessage { get; set; }

	public string StatusLabel => Status switch
	{
		"Pending" => "Chờ xác nhận",
		"Confirmed" => "Đã xác nhận",
		"Cancelled" => "Đã huỷ",
		"Arrived" => "Đã đến",
		"NoShow" => "Vắng mặt",
		"Rejected" => "Đã từ chối",
		_ => "Không xác định"
	};

	public string TableLabel =>
		string.IsNullOrWhiteSpace(TableCode)
			? "Chưa xếp bàn"
			: TableCode;

	public string AppointmentLabel =>
		StartsAtUtc
			.ToOffset(TimeSpan.FromHours(7))
			.ToString("HH:mm 'ngày' dd/MM/yyyy");

	public string? MaskedEmail
	{
		get
		{
			if (string.IsNullOrWhiteSpace(Email))
				return null;

			var atIndex = Email.IndexOf('@');

			if (atIndex <= 0)
				return "***";

			var localPart = Email[..atIndex];
			var domain = Email[atIndex..];

			if (localPart.Length == 1)
				return $"{localPart[0]}***{domain}";

			return $"{localPart[0]}{new string('*', Math.Max(3, localPart.Length - 1))}{domain}";
		}
	}
}