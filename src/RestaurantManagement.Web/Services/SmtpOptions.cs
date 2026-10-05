namespace RestaurantManagement.Web.Services
{
	public class SmtpOptions
	{
		public string Host { get; set; } = string.Empty;

		public int Port { get; set; } = 587;

		public string Username { get; set; } = string.Empty;

		public string Password { get; set; } = string.Empty;

		public string FromEmail { get; set; } = string.Empty;

		public string FromName { get; set; } = "Restaurant";

		public bool UseSsl { get; set; } = false;
	}
}