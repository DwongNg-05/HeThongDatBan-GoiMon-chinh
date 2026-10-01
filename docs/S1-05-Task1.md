# S1-05 Task 1 — Nhật ký đăng nhập và sửa giá món (chỉ Quản lý)

Mục tiêu: quản lý xem được nhật ký đăng nhập và sửa giá món cơ bản; chỉ vai trò Quản lý được truy cập.
Hoàn tất: một phần AC1 và một phần AC4. Phần “chỉ đọc” mới có ràng buộc ở database và màn hình chỉ xem, chưa rà soát/kiểm thử riêng.

## Kho dữ liệu riêng

Migration `020_SecurityAuditLog.sql` tạo bảng **`dbo.SecurityAuditLogs`**. Bảng này tách khỏi `dbo.AuditLogs`, vốn là nhật ký nghiệp vụ dạng JSON do các stored procedure ghi.

| Cột | Ý nghĩa |
| --- | --- |
| `OccurredAt` | Thời điểm, UTC, do SQL Server ghi (`SYSUTCDATETIME()`); màn hình hiển thị theo UTC+7 |
| `UserId`, `UserName` | Tài khoản. Với tài khoản có thật, tên lấy từ `dbo.Users`, kể cả khi đăng nhập bằng số điện thoại |
| `RoleCode` | Vai trò (`Manager`, `Waiter`, `Kitchen`, `Cashier`) lấy từ `dbo.Roles`; màn hình hiển thị tên tiếng Việt |
| `Action` | `LoginSucceeded`, `LoginFailed`, `PriceChanged` |
| `Detail` | Với sửa giá: `Tên món (#Id): 45.000 ₫ → 50.000 ₫` |
| `IpAddress` | IP của yêu cầu (`HttpContext.Connection.RemoteIpAddress`; IPv4 ánh xạ IPv6 được đổi về IPv4) |

Bảng **chỉ cho thêm**: trigger `tr_SecurityAuditLogs_AppendOnly` chặn UPDATE/DELETE với lỗi 51110. Không có màn hình hay API để sửa/xoá.

## Ghi nhận sự kiện

- **Đăng nhập thành công**: `AccountController.Login` ghi qua `dbo.usp_WriteLoginAudit` sau khi tạo phiên.
- **Đăng nhập thất bại**: ghi cho mọi lần bị từ chối, gồm sai mật khẩu, tài khoản đang bị khoá 15 phút, tài khoản ngừng hoạt động, định danh không tồn tại và biểu mẫu thiếu dữ liệu. Nếu định danh khớp một tài khoản, nhật ký lưu đúng tài khoản và vai trò của tài khoản đó.
  - Nếu định danh **không tồn tại**, nhật ký chỉ lưu 2 ký tự đầu, ví dụ `kh*** (không tồn tại)`, và vai trò “Không xác định”. Người dùng có thể gõ nhầm mật khẩu vào ô tên đăng nhập, và nhóm đã quy định không lưu nguyên văn định danh lạ (xem README phần khoá đăng nhập). Hệ thống không bao giờ lưu mật khẩu.
  - Thông báo trên màn hình đăng nhập không đổi (vẫn là lỗi chung).
- **Sửa giá món**: `dbo.usp_UpdateMenuPrice` được cập nhật (thêm tham số tuỳ chọn `@IpAddress`) để ghi nhật ký **trong cùng giao dịch** với việc đổi giá, nên không thể đổi giá mà thiếu nhật ký. Chỉ ghi khi giá thực sự thay đổi; lưu lại cùng một giá thì không sinh bản ghi. Cả hai nơi sửa giá đều được ghi:
  - **Sửa giá món** (`/Management`, liên kết mới trong menu của Quản lý);
  - **Quản lý món → Sửa** (`/QuanLyMon/Sua/{id}`).

  Tài khoản và vai trò lấy theo người đang đăng nhập phía máy chủ, không lấy từ biểu mẫu.

## Màn hình nhật ký

`/AuditLogs` (menu **Nhật ký hệ thống**, chỉ hiện với Quản lý) liệt kê tối đa 200 sự kiện gần nhất, **mới nhất lên đầu**, gồm các cột Thời điểm, Tài khoản, Vai trò, Hành động (kèm chi tiết giá cũ → mới) và Địa chỉ IP.

- **Giới hạn truy cập**: `[Authorize(Roles = "Manager")]`. Vai trò khác bị chuyển tới `/Account/AccessDenied` (403); người chưa đăng nhập bị chuyển tới trang đăng nhập. Thủ tục `usp_SecurityAuditList` kiểm tra thêm quyền `Audit.Read` ở database.
- Màn hình chỉ xem, không có biểu mẫu sửa/xoá.
- Trước đây màn hình này dùng EF (`ApplicationDbContext.AuditLogs`, `AuditLogService`) trỏ vào bảng `AuditLogs` có cấu trúc khác với bảng `dbo.AuditLogs` do migration SQL tạo, nên không chạy được trên database thật. Ngoài ra liên kết menu kiểm tra vai trò `QuanLy`, một mã vai trò không tồn tại. Hai lỗi này đã được thay bằng kho mới và vai trò `Manager`. Bộ lọc ngày/tài khoản cũ được bỏ khỏi màn hình vì thuộc task sau. `AuditLogService` (EF) không còn được dùng ở đâu.

## Chạy demo

```powershell
$env:RM_CONNECTION_STRING = 'Server=.\MSSQLSERVER07;Database=RestaurantManagement_Dev;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True'
dotnet run --project tools/RestaurantManagement.DbTool -- migrate      # áp dụng 020_SecurityAuditLog.sql
dotnet run --project src/RestaurantManagement.Web --launch-profile https
```

1. Đăng nhập sai mật khẩu với `manager`, rồi nhập một tên không tồn tại.
2. Đăng nhập đúng bằng `manager`.
3. Mở **Sửa giá món**, đổi giá một món và bấm **Lưu giá**.
4. Mở **Nhật ký hệ thống**: dòng trên cùng là sự kiện sửa giá, tiếp theo là đăng nhập thành công và các lần đăng nhập thất bại. Mỗi dòng có đủ thời điểm, tài khoản, vai trò, hành động và IP; chạy trên máy cá nhân thì IP là `127.0.0.1` hoặc `::1`.
5. Đăng xuất, đăng nhập bằng `waiter` rồi mở `/AuditLogs`: bị từ chối, và menu không có mục Nhật ký hệ thống.

## Kiểm thử

```powershell
dotnet build RestaurantManagement.sln --no-restore -m:1
dotnet run --project tests/RestaurantManagement.AreaTests --no-build
dotnet run --no-build --project tools/RestaurantManagement.DbTool -- verify
```

- `SecurityAuditVerification` (trong `verify`, SQL Server, đăng nhập thật qua HTTP):
  - **đăng nhập thất bại** (theo tên, theo số điện thoại, định danh không tồn tại) sinh đúng bản ghi: tài khoản, vai trò, `127.0.0.1`, thời điểm trong 1 phút; không lưu định danh lạ hay mật khẩu;
  - **đăng nhập thành công** sinh đúng bản ghi;
  - **sửa giá món** ở cả hai màn hình sinh đúng bản ghi kèm giá cũ → mới; lưu cùng một giá không sinh bản ghi;
  - màn hình hiển thị đủ cột, mới nhất lên đầu;
  - **tài khoản không phải quản lý** (`waiter`) bị chuyển tới AccessDenied (403) và không thấy liên kết; database cũng từ chối;
  - người chưa đăng nhập bị chuyển tới trang đăng nhập;
  - UPDATE/DELETE nhật ký bị chặn.
- `SecurityAuditTests` (AreaTests, không cần database): che định danh lạ, chuẩn hoá IP, giờ UTC+7 và nhãn tiếng Việt, `[Authorize(Roles = "Manager")]`, controller chỉ có GET.
- `AuthenticationHttpTests`: `/AuditLogs` bị chặn khi chưa đăng nhập.

## Chưa có trong task này

Bộ lọc theo ngày và theo tài khoản, mặc định 7 ngày gần nhất (đã làm ở [Task 2](S1-05-Task2.md)), phân trang, rà soát và kiểm thử riêng việc chặn sửa/xoá (AC4 đầy đủ), xử lý IP khi chạy sau proxy/reverse proxy (`X-Forwarded-For`).
