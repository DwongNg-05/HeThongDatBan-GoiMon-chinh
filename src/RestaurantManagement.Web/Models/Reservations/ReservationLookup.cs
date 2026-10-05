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
