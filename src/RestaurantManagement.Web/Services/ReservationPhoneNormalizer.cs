using System.Text.RegularExpressions;

namespace RestaurantManagement.Web.Services;

/// <summary>
/// Chuẩn hoá số điện thoại đặt bàn về đúng một dạng: 10 chữ số, bắt đầu bằng 0 (ví dụ 0901234567).
/// Mọi lượt đặt lưu số đã chuẩn hoá, nên lịch sử của cùng một khách (S3-09 Task 1: số lần không tới,
/// dbo.ReservationNoShowHistory) được gom về một chuỗi dù khách nhập theo nhiều cách:
/// "0901 234 567", "090.123.4567", "090-123-4567", "(090) 123 4567", "+84 901 234 567", "84901234567", "0084901234567".
/// </summary>
public static class ReservationPhoneNormalizer
{
	public static string? Normalize(string? input)
	{
		if (string.IsNullOrWhiteSpace(input))
			return null;

		// Bỏ khoảng trắng, dấu chấm, gạch nối và dấu ngoặc.
		var phone = Regex.Replace(input.Trim(), @"[\s.\-()]", "");

		// Đổi mã quốc gia Việt Nam (+84, 0084, 84) thành đầu 0.
		if (phone.StartsWith("+84", StringComparison.Ordinal))
			phone = "0" + phone[3..];
		else if (phone.StartsWith("0084", StringComparison.Ordinal))
			phone = "0" + phone[4..];
		else if (phone.Length == 11 && phone.StartsWith("84", StringComparison.Ordinal))
			phone = "0" + phone[2..];

		// Chấp nhận 10 chữ số, bắt đầu bằng 0.
		return Regex.IsMatch(phone, @"^0[0-9]{9}$")
			? phone
			: null;
	}
}
