using System.Text.RegularExpressions;

namespace RestaurantManagement.Web.Services;

public static class ReservationPhoneNormalizer
{
	public static string? Normalize(string? input)
	{
		if (string.IsNullOrWhiteSpace(input))
			return null;

		// Bỏ khoảng trắng và dấu chấm.
		var phone = Regex.Replace(input.Trim(), @"[\s.()\-]", "");

		// Đổi đầu +84 thành đầu 0.
		if (phone.StartsWith("+84", StringComparison.Ordinal))
		{
			phone = "0" + phone[3..];
		}
		else if (phone.StartsWith("0084", StringComparison.Ordinal)) phone = "0" + phone[4..];

		// Chấp nhận 10 chữ số, bắt đầu bằng 0.
		return Regex.IsMatch(phone, @"^0[0-9]{9}$")
			? phone
			: null;
	}
}
