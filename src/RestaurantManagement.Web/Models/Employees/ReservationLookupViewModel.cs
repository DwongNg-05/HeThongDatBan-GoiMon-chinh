using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace RestaurantManagement.Web.ViewModels;

public class ReservationLookupViewModel
{
	[Required(ErrorMessage = "Vui lòng nhập mã đặt bàn.")]
	[StringLength(30, ErrorMessage = "Mã đặt bàn quá dài.")]
	[Display(Name = "Mã đặt bàn")]
	public string Code { get; set; } = "";

	[Required(ErrorMessage = "Vui lòng nhập số điện thoại.")]
	[StringLength(50, ErrorMessage = "Số điện thoại quá dài.")]
	[Display(Name = "Số điện thoại")]
	public string Phone { get; set; } = "";

	[ValidateNever]
	public ReservationDetailsViewModel? Result { get; set; }

	[ValidateNever]
	public string? SuccessMessage { get; set; }
}