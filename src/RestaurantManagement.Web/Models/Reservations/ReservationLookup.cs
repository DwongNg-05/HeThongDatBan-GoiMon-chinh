using System.ComponentModel.DataAnnotations;
namespace RestaurantManagement.Web.Models.Reservations;

public static class RejectionReasons
{
    public static IReadOnlyDictionary<string,string> Labels { get; } = new System.Collections.ObjectModel.ReadOnlyDictionary<string,string>(
        new Dictionary<string,string>(StringComparer.Ordinal) { ["NoTable"]="Hết bàn",["OutsideHours"]="Ngoài giờ phục vụ",["Unreachable"]="Không liên lạc được" });
    public static string? CustomerText(string? code) => code switch
    {
        "NoTable" => "Nhà hàng đã hết bàn phù hợp trong khung giờ bạn chọn.",
        "OutsideHours" => "Thời gian đặt bàn nằm ngoài giờ phục vụ.",
        "Unreachable" => "Nhà hàng không liên lạc được với bạn để xác nhận đặt bàn.",
        _ => null
    };
}
public sealed class ReservationLookup
{
    [Required(ErrorMessage="Vui lòng nhập mã đặt bàn.")]
    [RegularExpression("^[A-Za-z0-9]{6}$",ErrorMessage="Mã đặt bàn gồm 6 chữ cái hoặc chữ số.")]
    [Display(Name="Mã đặt bàn")]
    public string Code { get; set; } = "";
    [Required(ErrorMessage="Vui lòng nhập số điện thoại đã đặt bàn.")]
    [RegularExpression("^0[0-9]{9}$",ErrorMessage="Số điện thoại gồm 10 chữ số, bắt đầu bằng 0.")]
    [Display(Name="Số điện thoại đặt bàn")]
    public string Phone { get; set; } = "";
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public CustomerReservation? Result { get; set; }
}
public sealed record CustomerReservation(string Code,string Status,string? RejectionReason,DateTime StartsAt,DateTime EndsAt,int GuestCount,string? TableCode)
{
    public string StatusLabel => new ReservationConfirmation { Status=Status }.StatusLabel;
    public string? RejectionText => Status=="Rejected"?RejectionReasons.CustomerText(RejectionReason):null;
}
