namespace RestaurantManagement.DbTool;

/// <summary>Một API (action MVC, Razor Page hoặc endpoint tối giản) và vai trò được phép gọi.</summary>
/// <param name="Key">
/// Định danh khớp với kiểm thử phản chiếu (ApiAuthorizationCoverageTests):
/// "{Controller}.{Action} {VERB|ANY}" cho MVC, "Page {đường dẫn}" cho Razor Page, "Minimal {VERB} {đường dẫn}" cho endpoint tối giản.
/// </param>
/// <param name="Allowed">"Anonymous" (ai cũng gọi được), "SignedIn" (chỉ cần đăng nhập) hoặc danh sách vai trò, ví dụ "Manager,Waiter".</param>
/// <param name="Method">Phương thức HTTP dùng khi gọi thử.</param>
/// <param name="Url">Đường dẫn mẫu để gọi thử (id không tồn tại: kiểm tra quyền diễn ra trước khi đọc dữ liệu).</param>
internal sealed record ApiEndpoint(string Key, string Allowed, string Method, string Url);

/// <summary>
/// S1-04 Task 4: bảng phân quyền của TOÀN BỘ API trong web — nguồn đối chiếu duy nhất cho
/// (1) kiểm thử phản chiếu: mọi action/page trong mã nguồn phải có mặt ở đây với đúng quyền, không thừa không thiếu;
/// (2) kiểm thử HTTP thật: gọi từng API khi chưa đăng nhập, với 4 vai trò và với vai trò không xác định.
/// Vai trò khớp dbo.RolePermissions (005_ReferenceData.sql, thu hẹp ở 031_RolePermissionsByScope.sql).
/// Chi tiết: docs/S1-04-Task4.md; phạm vi từng vai trò: docs/S1-04-TongHop.md.
/// </summary>
internal static class ApiAccessMatrix
{
    public const string Anonymous = "Anonymous";
    public const string SignedIn = "SignedIn";
    public const string Manager = "Manager";
    public const string FrontOfHouse = "Manager,Waiter";
    public const string AllStaff = "Manager,Waiter,Kitchen,Cashier";
    // S1-04 Task 2: phần việc của Bếp và Thu ngân. S1-04 Task 1: Phục vụ không xem màn hình bếp. Báo hết trong ngày chỉ ở Quản lý món (Management.Availability).
    // S2-08 Task 1: Bếp và Quản lý bật/tắt "Tạm hết" ở danh sách món trong ngày (Kitchen.Dishes, Kitchen.TemporarilyOut).
    public const string KitchenReaders = "Manager,Kitchen";
    public const string KitchenWorkers = "Kitchen";
    public const string Cashiers = "Manager,Cashier";

    public static readonly string[] Roles = ["Manager", "Waiter", "Kitchen", "Cashier"];

    private const string Missing = "999999";

    public static readonly IReadOnlyList<ApiEndpoint> All =
    [
        new("ReservationConfirmations.Index GET", FrontOfHouse, "GET", "/ReservationConfirmations/Index?id=999999"),
        new("ReservationConfirmations.Details GET", FrontOfHouse, "GET", "/ReservationConfirmations/Details?id=999999"),
        new("ReservationConfirmations.Schedule GET", FrontOfHouse, "GET", "/ReservationConfirmations/Schedule?id=999999"),
        new("ReservationConfirmations.ChangeTable POST", FrontOfHouse, "POST", "/ReservationConfirmations/ChangeTable?id=999999"),
        new("ReservationConfirmations.Confirm POST", FrontOfHouse, "POST", "/ReservationConfirmations/Confirm?id=999999"),
        new("ReservationConfirmations.Reject POST", FrontOfHouse, "POST", "/ReservationConfirmations/Reject?id=999999"),
        new("PublicReservations.Create GET", Anonymous, "GET", "/PublicReservations/Create"),
        new("PublicReservations.Create POST", Anonymous, "POST", "/PublicReservations/Create"),
        new("PublicReservations.Success GET", Anonymous, "GET", "/PublicReservations/Success"),
        new("PublicReservations.Areas GET", Anonymous, "GET", "/api/reservation-areas"),
        new("PublicReservations.Slots GET", Anonymous, "GET", "/api/reservation-slots"),
        new("ReservationLookup.Index GET", Anonymous, "GET", "/ReservationLookup/Index"),
        new("ReservationLookup.Lookup POST", Anonymous, "POST", "/ReservationLookup/Lookup"),
        new("ReservationLookup.Cancel POST", Anonymous, "POST", "/ReservationLookup/Cancel"),
        new("ReservationManagement.Index GET", FrontOfHouse, "GET", "/ReservationManagement"),
        new("ReservationManagement.Daily GET", FrontOfHouse, "GET", "/ReservationManagement/Daily"),
        new("ReservationManagement.Details GET", FrontOfHouse, "GET", "/ReservationManagement/Details/999999"),
        new("TableReservationSchedule.Index GET", Manager, "GET", "/TableReservationSchedule/Index"),
        new("TableReservationSchedule.Create GET", Manager, "GET", "/TableReservationSchedule/Create"),
        new("TableReservationSchedule.Create POST", Manager, "POST", "/TableReservationSchedule/Create"),
        new("TableReservationSchedule.Cancel POST", Manager, "POST", "/TableReservationSchedule/999999/Cancel"),
        new("TableReservationSchedule.MarkNoShow POST", Manager, "POST", "/TableReservationSchedule/999999/NoShow"),
        new("TableMap.Index GET", FrontOfHouse, "GET", "/TableMap"),
        new("TableMap.Snapshot GET", FrontOfHouse, "GET", "/api/table-map/snapshot"),
        new("TableMap.Changes GET", FrontOfHouse, "GET", "/api/table-map/changes"),
        new("TemporaryOut.Availability GET", Manager, "GET", "/menu/availability"),
        new("TemporaryOut.SetAvailability POST", Manager, "POST", "/menu/999999/availability"),
        new("TemporaryOut.SetTemporarilyOut POST", Manager, "POST", "/menu/999999/temporarily-out"),
        new("TemporaryOut.TemporarilyHistory GET", Manager, "GET", "/menu/999999/temporarily-history"),
        // ---- Tài khoản ----
        new("Account.Login GET", Anonymous, "GET", "/Account/Login"),
        new("Account.Login POST", Anonymous, "POST", "/Account/Login"),
        new("Account.AccessDenied ANY", Anonymous, "GET", "/Account/AccessDenied"),
        new("Account.VerifyEmail GET", AllStaff, "GET", "/Account/VerifyEmail"),
        new("Account.VerifyEmail POST", AllStaff, "POST", "/Account/VerifyEmail"),
        new("Account.ResendVerificationCode POST", AllStaff, "POST", "/Account/ResendVerificationCode"),
        new("Account.SetVerificationEmail POST", AllStaff, "POST", "/Account/SetVerificationEmail"),
        new("Account.ChangePassword GET", AllStaff, "GET", "/Account/ChangePassword"),
        new("Account.ChangePassword POST", AllStaff, "POST", "/Account/ChangePassword"),
        new("Account.Logout POST", SignedIn, "POST", "/Account/Logout"),
        new("Account.Me GET", AllStaff, "GET", "/api/account/me"),

        // ---- Quản trị ----
        new("AuditLogs.Index GET", Manager, "GET", "/AuditLogs"),
        new("EmployeeAccounts.Index GET", Manager, "GET", "/admin/employee-accounts"),
        new("EmployeeAccounts.Create GET", Manager, "GET", "/admin/employee-accounts/create"),
        new("EmployeeAccounts.Create POST", Manager, "POST", "/admin/employee-accounts/create"),
        new("EmployeeAccounts.CheckUserName GET", Manager, "GET", "/admin/employee-accounts/check-username?userName=s104"),
        new("EmployeeAccounts.CheckUserName POST", Manager, "POST", "/admin/employee-accounts/check-username?userName=s104"),
        new("EmployeeAccounts.CheckPhoneNumber GET", Manager, "GET", "/admin/employee-accounts/check-phone-number?phoneNumber=0900000000"),
        new("EmployeeAccounts.CheckPhoneNumber POST", Manager, "POST", "/admin/employee-accounts/check-phone-number?phoneNumber=0900000000"),
        new("EmployeeAccounts.Deactivate POST", Manager, "POST", $"/admin/employee-accounts/{Missing}/deactivate"),

        // ---- Trang chủ / sơ đồ bàn ----
        new("Home.Index ANY", FrontOfHouse, "GET", "/Home/Index"),
        new("Home.Privacy ANY", Manager, "GET", "/Home/Privacy"),
        new("Home.Error ANY", Anonymous, "GET", "/Home/Error"),
        new("TableDetails.Get GET", FrontOfHouse, "GET", "/api/table-map/ZZ99"),
        new("TableStatus.Snapshot GET", FrontOfHouse, "GET", "/api/table-status"),
        new("TableStatus.Stream GET", FrontOfHouse, "GET", "/api/table-status/stream"),
        new("TableStatus.Update POST", FrontOfHouse, "POST", "/api/table-status/ZZ99"),

        // ---- Thực đơn công khai, QR bàn ----
        new("Menu.Index GET", Anonymous, "GET", "/Menu"),
        new("MenuApi.List GET", Anonymous, "GET", "/api/menu"),
        new("MenuApi.Availability GET", Anonymous, "GET", "/api/menu/availability"),
        new("TableQr.Scan GET", Anonymous, "GET", "/q/khong-ton-tai"),

        // ---- Quản lý món ----
        new("Management.Index ANY", Manager, "GET", "/Management"),
        new("Management.Availability POST", Manager, "POST", "/Management/Availability"),
        new("Page /Dishes/Index", Manager, "GET", "/Dishes"),
        new("Page /Dishes/Create", Manager, "GET", "/Dishes/Create"),
        new("Page /Dishes/Edit", Manager, "GET", $"/Dishes/Edit/{Missing}"),
        new("Page /Dishes/PriceHistory", Manager, "GET", "/Dishes/PriceHistory/1"),
        new("Page /DishCategories/Index", Manager, "GET", "/DishCategories"),
        new("Page /DishCategories/Create", Manager, "GET", "/DishCategories/Create"),
        new("Page /DishCategories/Edit", Manager, "GET", $"/DishCategories/Edit/{Missing}"),
        new("Page /DishCategories/Delete", Manager, "GET", $"/DishCategories/Delete/{Missing}"),
        new("Page /DishCategories/Reorder", Manager, "GET", "/DishCategories/Reorder"),
        new("Page /DishCategories/Status", Manager, "GET", $"/DishCategories/Status/{Missing}"),

        // ---- Gọi món ----
        new("Page /Ordering/Index", FrontOfHouse, "GET", "/Ordering"),
        new("Page /Ordering/Checkout", FrontOfHouse, "POST", "/Ordering/Checkout"),
        new("Page /Orders/Details", FrontOfHouse, "GET", $"/Orders/Details/{Missing}"),

        // ---- Bếp (S1-04 Task 2) ----
        new("Kitchen.Index GET", KitchenReaders, "GET", "/Kitchen"),
        new("Kitchen.Ready GET", "Manager,Waiter", "GET", "/Kitchen/Ready"),
        new("Kitchen.Snapshot GET", "Manager,Kitchen,Waiter", "GET", "/Kitchen/Snapshot"),
        new("Kitchen.Transition POST", KitchenWorkers, "POST", "/Kitchen/Transition"),
        new("Kitchen.Advance POST", KitchenWorkers, "POST", $"/Kitchen/Advance/{Missing}"),
        new("Kitchen.Dishes GET", KitchenReaders, "GET", "/Kitchen/Dishes"),
        // S2-08 Task 2: lịch sử bật/tắt "Tạm hết" — chỉ Quản lý.
        new("Kitchen.History GET", Manager, "GET", "/Kitchen/Dishes/History"),
        new("Kitchen.TemporarilyOut POST", KitchenReaders, "POST", $"/Kitchen/Dishes/{Missing}/TemporarilyOut"),

        // ---- Thu ngân (S1-04 Task 2) ----
        new("Cashier.Index GET", Cashiers, "GET", "/Cashier"),
        new("Cashier.Checkout POST", Cashiers, "POST", "/Cashier/Checkout"),
        new("Cashier.Invoices GET", Cashiers, "GET", "/Cashier/Invoices"),
        new("Cashier.Shift GET", Cashiers, "GET", "/Cashier/Shift"),
        new("Cashier.OpenShift POST", Cashiers, "POST", "/Cashier/OpenShift"),
        new("Cashier.CloseShift POST", Cashiers, "POST", "/Cashier/CloseShift"),

        // ---- Đặt bàn ----
        new("Reservations.Index GET", FrontOfHouse, "GET", "/Reservations"),
        new("Reservations.Details GET", FrontOfHouse, "GET", $"/Reservations/Details/{Missing}"),
        new("Reservations.EmailStatus GET", FrontOfHouse, "GET", $"/Reservations/EmailStatus/{Missing}"),
        new("Reservations.Create GET", Anonymous, "GET", "/Reservations/Create"),
        new("Reservations.Create POST", Anonymous, "POST", "/Reservations/Create"),
        new("Reservations.Success GET", Anonymous, "GET", "/Reservations/Success"),
        new("Reservations.BookingEmailStatus GET", Anonymous, "GET", "/Reservations/BookingEmailStatus"),
        new("Reservations.CheckSchedule GET", Anonymous, "GET", "/Reservations/CheckSchedule"),
        new("Reservations.AvailableTables GET", Anonymous, "GET", "/Reservations/AvailableTables"),
        // Khách tự đặt bàn và tự xác nhận qua nút trong email, không cần đăng nhập.
        new("Reservations.Confirm GET", Anonymous, "GET", "/Reservations/Confirm"),
        new("Reservations.Confirm POST", Anonymous, "POST", "/Reservations/Confirm"),
        new("Reservations.Cancel POST", Manager, "POST", $"/Reservations/Cancel/{Missing}"),
        new("Reservations.Delete POST", Manager, "POST", $"/Reservations/Delete/{Missing}"),

        // ---- Giờ hoạt động, ngày nghỉ ----
        new("OpeningHours.Index GET", Manager, "GET", "/OpeningHours"),
        new("OpeningHours.Index POST", Manager, "POST", "/OpeningHours"),
        new("OpeningHours.Calendar GET", Manager, "GET", "/OpeningHours/Calendar"),
        new("SpecialHolidays.Index GET", Manager, "GET", "/SpecialHolidays"),
        new("SpecialHolidays.Create GET", Manager, "GET", "/SpecialHolidays/Create"),
        new("SpecialHolidays.Create POST", Manager, "POST", "/SpecialHolidays/Create"),
        new("SpecialHolidays.Edit GET", Manager, "GET", $"/SpecialHolidays/Edit/{Missing}"),
        new("SpecialHolidays.Edit POST", Manager, "POST", $"/SpecialHolidays/Edit/{Missing}"),
        new("SpecialHolidays.Delete GET", Manager, "GET", $"/SpecialHolidays/Delete/{Missing}"),
        new("SpecialHolidays.Delete POST", Manager, "POST", $"/SpecialHolidays/Delete/{Missing}"),

        // ---- Khu vực & bàn, mã QR ----
        new("Areas.Index GET", Manager, "GET", "/Areas"),
        new("Areas.Create GET", Manager, "GET", "/Areas/Create"),
        new("Areas.Create POST", Manager, "POST", "/Areas/Create"),
        new("Areas.Edit GET", Manager, "GET", $"/Areas/Edit/{Missing}"),
        new("Areas.Edit POST", Manager, "POST", "/Areas/Edit"),
        new("Areas.Deactivate GET", Manager, "GET", $"/Areas/Deactivate/{Missing}"),
        new("Areas.Deactivate POST", Manager, "POST", $"/Areas/Deactivate/{Missing}"),
        new("Areas.Delete POST", Manager, "POST", $"/Areas/Delete/{Missing}"),
        new("Areas.Reactivate POST", Manager, "POST", $"/Areas/Reactivate/{Missing}"),
        new("Tables.Index GET", Manager, "GET", "/Tables"),
        new("Tables.Create GET", Manager, "GET", "/Tables/Create"),
        new("Tables.Create POST", Manager, "POST", "/Tables/Create"),
        new("Tables.Edit GET", Manager, "GET", $"/Tables/Edit/{Missing}"),
        new("Tables.Edit POST", Manager, "POST", "/Tables/Edit"),
        new("Tables.Delete POST", Manager, "POST", $"/Tables/Delete/{Missing}"),
        new("Tables.Details GET", Manager, "GET", $"/Tables/Details/{Missing}"),
        new("Tables.QrImage GET", Manager, "GET", $"/Tables/QrImage/{Missing}"),
        new("Tables.DownloadQrImage GET", Manager, "GET", $"/Tables/DownloadQrImage/{Missing}"),
        new("Tables.GenerateQr POST", Manager, "POST", $"/Tables/GenerateQr/{Missing}"),
        new("Tables.RotateQr POST", Manager, "POST", $"/Tables/RotateQr/{Missing}"),
        new("Tables.DownloadQrPdf POST", Manager, "POST", "/Tables/DownloadQrPdf?areaId=" + Missing),
        new("Tables.CheckCode GET", Manager, "GET", "/Tables/CheckCode?code=ZZ99"),

        // ---- Endpoint tối giản trong Program.cs: chuyển hướng đường dẫn tiếng Việt cũ ----
        new("Minimal GET /ThucDon", Anonymous, "GET", "/ThucDon"),
        new("Minimal GET /api/thuc-don", Anonymous, "GET", "/api/thuc-don"),
        new("Minimal GET /GoiMon", Anonymous, "GET", "/GoiMon"),
        new("Minimal GET /QuanLyMon", Anonymous, "GET", "/QuanLyMon"),
        new("Minimal GET /QuanLyNhomMon", Anonymous, "GET", "/QuanLyNhomMon"),
    ];

    /// <summary>Vai trò này có được gọi API không ("Guest" = vai trò không xác định, null = chưa đăng nhập).</summary>
    public static bool IsAllowed(ApiEndpoint endpoint, string? role) => endpoint.Allowed switch
    {
        Anonymous => true,
        SignedIn => role is not null,
        _ => role is not null && endpoint.Allowed.Split(',').Contains(role)
    };
}
