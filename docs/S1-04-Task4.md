# S1-04 Task 4: Kiểm tra quyền ở máy chủ cho toàn bộ API

**Mục tiêu:** mọi API trong hệ thống đều được kiểm tra quyền ở máy chủ, không chỉ các API đã nêu trong tiêu chí. API ở đây gồm action MVC, Razor Page, API JSON và endpoint tối giản. **Hoàn tất AC4.**

## Kết quả rà soát

Trước task này, nhiều API chỉ dựa vào chính sách dự phòng “đã đăng nhập là được”. Kết quả là Bếp hay Thu ngân cũng gọi được các API quản lý (khu vực, bàn, mã QR, giờ hoạt động, ngày nghỉ, nhóm món, thêm/sửa món, gọi món, tạo đặt bàn, đổi trạng thái bàn).

Ngoài ra, khu vực, bàn, mã QR, giờ hoạt động và ngày nghỉ ghi thao tác dưới tài khoản lấy từ **cấu hình** (`AreaManagement:ActorUserId`, `OpeningHours:ActorUserId`), không phải tài khoản đang đăng nhập.

| Nhóm | Thay đổi |
| --- | --- |
| Vai trò dùng chung | `Security/AppRoles.cs` (mới): `Manager`, `Waiter`, `Kitchen`, `Cashier`, cùng các nhóm `FrontOfHouse` (Quản lý, Phục vụ), `AllStaff` (cả 4), `KitchenReaders` (Quản lý, Bếp), `KitchenWorkers` (Bếp), `Cashiers` (Quản lý, Thu ngân). Các nhóm khớp `dbo.RolePermissions` sau migration `031_RolePermissionsByScope.sql` và `032_RemoveKitchenDishes.sql`. |
| Gắn quyền cho từng API | `[Authorize(Roles = …)]` cho mọi controller và Razor Page theo bảng dưới. Không còn API nào chỉ dựa vào cấu hình mặc định. |
| Vai trò không xác định | Chính sách mặc định và dự phòng yêu cầu **đã đăng nhập và có một trong 4 vai trò**. Tài khoản có vai trò lạ, hoặc không có vai trò, bị chặn ở mọi API; chỉ còn đăng xuất được (chính sách `SignedIn`). |
| Chưa đăng nhập / sai vai trò | Chưa đăng nhập: trang HTML được chuyển tới đăng nhập. Sai vai trò: trang HTML trả **403** ngay tại đường dẫn, kèm trang “Không có quyền truy cập” (S1-04 Task 3). API JSON (`/api/...` hoặc fetch có `X-Requested-With`) trả **401** / **403**, không chuyển hướng sang trang HTML; từ S1-04 Task 1, cả hai mã lỗi kèm dữ liệu lỗi JSON thuần (`status`, `error`, `message`, `role`, `path`). |
| Người thực hiện | Khu vực, bàn, mã QR, giờ hoạt động, ngày nghỉ dùng **Id tài khoản đang đăng nhập** làm `ActorUserId`. Thủ tục SQL kiểm tra lại quyền của chính tài khoản đó. |
| Thứ tự kiểm tra | Bước xác minh email và bắt đổi mật khẩu chạy **trước** bước phân quyền, nên tài khoản chưa hoàn tất luôn được đưa tới đúng màn hình đó. |
| Giao diện | Menu bên trái và nút “+ Đặt bàn mới” chỉ hiện với vai trò được dùng. Máy chủ vẫn chặn khi gõ thẳng đường dẫn. |

## Bảng phân quyền (101 API)

Nguồn đối chiếu duy nhất là `tools/RestaurantManagement.DbTool/ApiAccessMatrix.cs`. Bảng dưới được sinh từ tệp đó và đã cập nhật theo S1-04 Task 2 (phần việc của Bếp và Thu ngân) và S1-04 Task 1 (phạm vi của Phục vụ).

| API | Gọi thử | Được phép |
| --- | --- | --- |
| `Account.Login GET` | GET `/Account/Login` | Công khai |
| `Account.Login POST` | POST `/Account/Login` | Công khai |
| `Account.AccessDenied ANY` | GET `/Account/AccessDenied` | Công khai |
| `Account.VerifyEmail GET` | GET `/Account/VerifyEmail` | Quản lý, Phục vụ, Bếp, Thu ngân |
| `Account.VerifyEmail POST` | POST `/Account/VerifyEmail` | Quản lý, Phục vụ, Bếp, Thu ngân |
| `Account.ResendVerificationCode POST` | POST `/Account/ResendVerificationCode` | Quản lý, Phục vụ, Bếp, Thu ngân |
| `Account.SetVerificationEmail POST` | POST `/Account/SetVerificationEmail` | Quản lý, Phục vụ, Bếp, Thu ngân |
| `Account.ChangePassword GET` | GET `/Account/ChangePassword` | Quản lý, Phục vụ, Bếp, Thu ngân |
| `Account.ChangePassword POST` | POST `/Account/ChangePassword` | Quản lý, Phục vụ, Bếp, Thu ngân |
| `Account.Logout POST` | POST `/Account/Logout` | Đã đăng nhập (mọi vai trò) |
| `Account.Me GET` | GET `/api/account/me` | Quản lý, Phục vụ, Bếp, Thu ngân |
| `AuditLogs.Index GET` | GET `/AuditLogs` | Quản lý |
| `EmployeeAccounts.Index GET` | GET `/admin/employee-accounts` | Quản lý |
| `EmployeeAccounts.Create GET` | GET `/admin/employee-accounts/create` | Quản lý |
| `EmployeeAccounts.Create POST` | POST `/admin/employee-accounts/create` | Quản lý |
| `EmployeeAccounts.CheckUserName GET` | GET `/admin/employee-accounts/check-username?userName=s104` | Quản lý |
| `EmployeeAccounts.CheckUserName POST` | POST `/admin/employee-accounts/check-username?userName=s104` | Quản lý |
| `EmployeeAccounts.CheckPhoneNumber GET` | GET `/admin/employee-accounts/check-phone-number?phoneNumber=0900000000` | Quản lý |
| `EmployeeAccounts.CheckPhoneNumber POST` | POST `/admin/employee-accounts/check-phone-number?phoneNumber=0900000000` | Quản lý |
| `EmployeeAccounts.Deactivate POST` | POST `/admin/employee-accounts/{id}/deactivate` | Quản lý |
| `Home.Index ANY` | GET `/Home/Index` | Quản lý, Phục vụ |
| `Home.Privacy ANY` | GET `/Home/Privacy` | Quản lý |
| `Home.Error ANY` | GET `/Home/Error` | Công khai |
| `TableDetails.Get GET` | GET `/api/table-map/ZZ99` | Quản lý, Phục vụ |
| `TableStatus.Snapshot GET` | GET `/api/table-status` | Quản lý, Phục vụ |
| `TableStatus.Stream GET` | GET `/api/table-status/stream` | Quản lý, Phục vụ |
| `TableStatus.Update POST` | POST `/api/table-status/ZZ99` | Quản lý, Phục vụ |
| `Menu.Index GET` | GET `/Menu` | Công khai |
| `MenuApi.List GET` | GET `/api/menu` | Công khai |
| `TableQr.Scan GET` | GET `/q/khong-ton-tai` | Công khai |
| `Management.Index ANY` | GET `/Management` | Quản lý |
| `Management.Availability POST` | POST `/Management/Availability` | Quản lý |
| `Page /Dishes/Index` | GET `/Dishes` | Quản lý |
| `Page /Dishes/Create` | GET `/Dishes/Create` | Quản lý |
| `Page /Dishes/Edit` | GET `/Dishes/Edit/{id}` | Quản lý |
| `Page /Dishes/PriceHistory` | GET `/Dishes/PriceHistory/1` | Quản lý |
| `Page /DishCategories/Index` | GET `/DishCategories` | Quản lý |
| `Page /DishCategories/Create` | GET `/DishCategories/Create` | Quản lý |
| `Page /DishCategories/Edit` | GET `/DishCategories/Edit/{id}` | Quản lý |
| `Page /DishCategories/Delete` | GET `/DishCategories/Delete/{id}` | Quản lý |
| `Page /DishCategories/Reorder` | GET `/DishCategories/Reorder` | Quản lý |
| `Page /DishCategories/Status` | GET `/DishCategories/Status/{id}` | Quản lý |
| `Page /Ordering/Index` | GET `/Ordering` | Quản lý, Phục vụ |
| `Page /Ordering/Checkout` | POST `/Ordering/Checkout` | Quản lý, Phục vụ |
| `Page /Orders/Details` | GET `/Orders/Details/{id}` | Quản lý, Phục vụ |
| `Kitchen.Index GET` | GET `/Kitchen` | Quản lý, Bếp |
| `Kitchen.Advance POST` | POST `/Kitchen/Advance/{id}` | Bếp |
| `Cashier.Index GET` | GET `/Cashier` | Quản lý, Thu ngân |
| `Cashier.Checkout POST` | POST `/Cashier/Checkout` | Quản lý, Thu ngân |
| `Cashier.Invoices GET` | GET `/Cashier/Invoices` | Quản lý, Thu ngân |
| `Cashier.Shift GET` | GET `/Cashier/Shift` | Quản lý, Thu ngân |
| `Cashier.OpenShift POST` | POST `/Cashier/OpenShift` | Quản lý, Thu ngân |
| `Cashier.CloseShift POST` | POST `/Cashier/CloseShift` | Quản lý, Thu ngân |
| `Reservations.Index GET` | GET `/Reservations` | Quản lý, Phục vụ |
| `Reservations.Details GET` | GET `/Reservations/Details/{id}` | Quản lý, Phục vụ |
| `Reservations.EmailStatus GET` | GET `/Reservations/EmailStatus/{id}` | Quản lý, Phục vụ |
| `Reservations.Create GET` | GET `/Reservations/Create` | Quản lý, Phục vụ |
| `Reservations.Create POST` | POST `/Reservations/Create` | Quản lý, Phục vụ |
| `Reservations.Success GET` | GET `/Reservations/Success` | Quản lý, Phục vụ |
| `Reservations.BookingEmailStatus GET` | GET `/Reservations/BookingEmailStatus` | Quản lý, Phục vụ |
| `Reservations.CheckSchedule GET` | GET `/Reservations/CheckSchedule` | Quản lý, Phục vụ |
| `Reservations.AvailableTables GET` | GET `/Reservations/AvailableTables` | Quản lý, Phục vụ |
| `Reservations.Cancel POST` | POST `/Reservations/Cancel/{id}` | Quản lý |
| `Reservations.Delete POST` | POST `/Reservations/Delete/{id}` | Quản lý |
| `OpeningHours.Index GET` | GET `/OpeningHours` | Quản lý |
| `OpeningHours.Index POST` | POST `/OpeningHours` | Quản lý |
| `OpeningHours.Calendar GET` | GET `/OpeningHours/Calendar` | Quản lý |
| `SpecialHolidays.Index GET` | GET `/SpecialHolidays` | Quản lý |
| `SpecialHolidays.Create GET` | GET `/SpecialHolidays/Create` | Quản lý |
| `SpecialHolidays.Create POST` | POST `/SpecialHolidays/Create` | Quản lý |
| `SpecialHolidays.Edit GET` | GET `/SpecialHolidays/Edit/{id}` | Quản lý |
| `SpecialHolidays.Edit POST` | POST `/SpecialHolidays/Edit/{id}` | Quản lý |
| `SpecialHolidays.Delete GET` | GET `/SpecialHolidays/Delete/{id}` | Quản lý |
| `SpecialHolidays.Delete POST` | POST `/SpecialHolidays/Delete/{id}` | Quản lý |
| `Areas.Index GET` | GET `/Areas` | Quản lý |
| `Areas.Create GET` | GET `/Areas/Create` | Quản lý |
| `Areas.Create POST` | POST `/Areas/Create` | Quản lý |
| `Areas.Edit GET` | GET `/Areas/Edit/{id}` | Quản lý |
| `Areas.Edit POST` | POST `/Areas/Edit` | Quản lý |
| `Areas.Deactivate GET` | GET `/Areas/Deactivate/{id}` | Quản lý |
| `Areas.Deactivate POST` | POST `/Areas/Deactivate/{id}` | Quản lý |
| `Areas.Delete POST` | POST `/Areas/Delete/{id}` | Quản lý |
| `Areas.Reactivate POST` | POST `/Areas/Reactivate/{id}` | Quản lý |
| `Tables.Index GET` | GET `/Tables` | Quản lý |
| `Tables.Create GET` | GET `/Tables/Create` | Quản lý |
| `Tables.Create POST` | POST `/Tables/Create` | Quản lý |
| `Tables.Edit GET` | GET `/Tables/Edit/{id}` | Quản lý |
| `Tables.Edit POST` | POST `/Tables/Edit` | Quản lý |
| `Tables.Delete POST` | POST `/Tables/Delete/{id}` | Quản lý |
| `Tables.Details GET` | GET `/Tables/Details/{id}` | Quản lý |
| `Tables.QrImage GET` | GET `/Tables/QrImage/{id}` | Quản lý |
| `Tables.DownloadQrImage GET` | GET `/Tables/DownloadQrImage/{id}` | Quản lý |
| `Tables.GenerateQr POST` | POST `/Tables/GenerateQr/{id}` | Quản lý |
| `Tables.RotateQr POST` | POST `/Tables/RotateQr/{id}` | Quản lý |
| `Tables.DownloadQrPdf POST` | POST `/Tables/DownloadQrPdf?areaId={id}` | Quản lý |
| `Tables.CheckCode GET` | GET `/Tables/CheckCode?code=ZZ99` | Quản lý |
| `Minimal GET /ThucDon` | GET `/ThucDon` | Công khai |
| `Minimal GET /api/thuc-don` | GET `/api/thuc-don` | Công khai |
| `Minimal GET /GoiMon` | GET `/GoiMon` | Công khai |
| `Minimal GET /QuanLyMon` | GET `/QuanLyMon` | Công khai |
| `Minimal GET /QuanLyNhomMon` | GET `/QuanLyNhomMon` | Công khai |

Vai trò khớp quyền trong database (`dbo.RolePermissions`, đã thu hẹp ở `031_RolePermissionsByScope.sql`; tổng hợp: docs/S1-04-TongHop.md):
- **Quản lý:** toàn bộ, trừ chuyển trạng thái bếp.
- **Phục vụ:** chỉ sơ đồ bàn (đón khách, đổi trạng thái bàn), đặt bàn, gọi món. Bị chặn ở API thực đơn (quản lý món, nhóm món, tạm hết), tài khoản nhân viên, báo cáo (nhật ký, hoá đơn, chốt ca) và màn hình bếp (S1-04 Task 1).
- **Bếp:** màn hình bếp (chỉ Bếp chuyển trạng thái chế biến). Báo món tạm hết chỉ ở Quản lý món (Quản lý); màn hình “Món trong ngày” đã bỏ.
- **Thu ngân:** thanh toán, hoá đơn, mở/chốt ca.
- **Bếp, Thu ngân** bị chặn hẳn ở đặt bàn (kể cả xem danh sách, chi tiết, trạng thái email).

“Báo cáo” trong phạm vi hiện tại gồm nhật ký hệ thống (`AuditLogs`), hoá đơn và chốt ca (`Cashier.Invoices`, `Cashier.Shift`).

## Kiểm thử

| Bộ kiểm thử | Lệnh | Kiểm tra |
| --- | --- | --- |
| `ApiAuthorizationCoverageTests` (không cần database) | `dotnet run --project tests/RestaurantManagement.AreaTests --no-build` | Tìm **mọi** action MVC và Razor Page đã biên dịch bằng phản chiếu, rồi so với bảng phân quyền. Không API nào thiếu trong bảng, không mục thừa. Mọi API không công khai đều gắn `[Authorize]`; `[AllowAnonymous]` chỉ có ở API công khai. Chạy đúng chính sách phân quyền của web cho 7 trường hợp: chưa đăng nhập, 4 vai trò, vai trò không xác định, không có vai trò. Kết quả phải khớp bảng. |
| `ApiAuthorizationVerification` (web thật + SQL Server thật) | `dotnet run --no-build --project tools/RestaurantManagement.DbTool -- verify-api-permissions` (cũng chạy trong `verify`) | Gọi HTTP **toàn bộ 101 API** cho 6 trường hợp: chưa đăng nhập, Quản lý, Phục vụ, Bếp, Thu ngân, và một tài khoản vai trò không xác định tạo riêng cho bài kiểm tra. API ngoài quyền phải bị chặn (401/403, hoặc chuyển tới đăng nhập/từ chối truy cập); API trong quyền phải qua được bước kiểm tra quyền. Lời gọi ghi cố tình thiếu mã chống giả mạo nên dừng ở 400, không đổi dữ liệu. Sau khi gọi hết, phiên đăng nhập vẫn còn hiệu lực. Kiểm tra thêm: API trả 401 khi chưa đăng nhập; Bếp đổi trạng thái bàn qua API nhận 403; database không còn quyền rộng hơn web. Cùng lệnh này chạy luôn phần kiểm tra của Task 1 (Phục vụ) và Task 3 (gõ thẳng đường dẫn). |

Khi thêm một API mới mà quên ghi vào bảng hoặc quên gắn quyền, `ApiAuthorizationCoverageTests` báo lỗi ngay, kèm tên API.

## Demo

```powershell
dotnet build RestaurantManagement.sln -m:1
dotnet run --project tests/RestaurantManagement.AreaTests --no-build
dotnet run --no-build --project tools/RestaurantManagement.DbTool -- verify-api-permissions
```

Kết quả mong đợi:
- Một dòng `PASS` cho mỗi trường hợp, ví dụ `PASS: S1-04 Task 4 — Bếp: 101 API, được phép …, bị chặn …`.
- Một dòng tổng kết: `PASS: S1-04 Task 4 — 606 lời gọi (101 API × 6 trường hợp) đều đúng quyền.`

Nếu có API sai quyền, lệnh in từng dòng lỗi gồm vai trò, đường dẫn, mong đợi và thực tế.
