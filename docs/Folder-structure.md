# Cấu trúc thư mục theo chức năng

Controllers, Models và Services được chia theo **chức năng**: mỗi chức năng có một thư mục con cùng tên ở cả ba lớp, giống cách lớp View chia thư mục.

| Thư mục | Chức năng | Controllers | Models | Services |
|---|---|---|---|---|
| `Account` | Đăng nhập, xác minh email, đổi mật khẩu | AccountController | LoginModel, LoginResult, LoginUser, LoginSessionStore, PasswordChangeStore, EmailVerificationViewModel | EmailSender, EmailVerificationService/Store/Options, VerificationCode, VerificationEmail |
| `Employees` | Tài khoản nhân viên | EmployeeAccountsController | CreateEmployeeAccountViewModel | – |
| `Menu` | Thực đơn, quản lý món, nhóm món, tạm hết món | MenuController, MenuApiController, ManagementController | ManagementStore, ManagementMenu, MenuItem, MenuChange | IMenuStore, SqlMenuStore, InMemoryMenuStore, PublicMenu, DishImageUpload, CategoryNameRules, CategoryWithDishes |
| `Tables` | Khu vực, bàn, sơ đồ bàn, trạng thái bàn, mã QR | AreasController, TablesController, TableDetailsController, TableStatusController, TableQrController | AreaViewModels, DiningTable*ViewModel, TableQr*ViewModel, TableDetailsViewModel, TableMapViewModel | TableQrService, TableQrPdfBuilder, TableDetailsService, TableMapEventBroker, TableStatusOutboxWorker, DemoTableCatalog |
| `Reservations` | Đặt bàn, giờ hoạt động, ngày nghỉ, trạng thái email xác nhận | ReservationsController, OpeningHoursController, SpecialHolidaysController | ReservationViewModels, ReservationEmailStatus, BookingTime, VietnamTime, VietnamBookingTimeBinder, OpeningHoursViewModel, SpecialHolidayViewModel | ReservationEmailStatusStore |
| `Audit` | Nhật ký hệ thống, nhật ký bảo mật | AuditLogsController | AuditLog, SecurityAuditFilter, SecurityAuditStore | AuditLogService |
| `Home` / `Shared` | Trang chủ, trang lỗi | HomeController | ErrorViewModel (`Models/Shared`) | – |

Project `RestaurantManagement.Data`: `Models/Menu` (Dish, DishCategory, PriceChangeLog), `Models/Orders` (Order, OrderLine), `Models/Audit` (AuditLog), `Models/Account` (User).
Các thư mục `Entities`, `EmployeeAccounts`, `Migrations` được giữ nguyên.

Ghi chú:
- Chỉ **di chuyển file**, không đổi `namespace`, nên mọi `using`, `@model`, test và tool vẫn dùng như cũ.
- Thư mục cũ `ViewModels`, `Models/Areas`, `Services/EmailVerification` đã được gộp vào bảng trên.
- Khi thêm chức năng mới: tạo thư mục cùng tên trong `Controllers/`, `Models/`, `Services/` (và `Views/` hoặc `Pages/`).
