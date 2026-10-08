# S1-04 Task 1: Phục vụ chỉ thấy đúng phần việc của mình

**Mục tiêu:** tài khoản Phục vụ đăng nhập chỉ thấy đúng phần việc của mình. Khi cố truy cập ngoài phạm vi, máy chủ chặn lại. **Hoàn tất một phần AC1 (đúng menu Phục vụ) và bước đầu của AC4 (kiểm tra ở máy chủ).**

## Vai trò và bảng ánh xạ

| Thành phần | Nội dung |
| --- | --- |
| Danh sách vai trò | `Security/AppRoles.cs`: `Manager` (Quản lý), `Waiter` (Phục vụ), `Kitchen` (Bếp), `Cashier` (Thu ngân). Mã khớp `dbo.Roles.Code`. `AppRoles.DisplayName` trả tên tiếng Việt. |
| Bảng vai trò → màn hình | `Security/RoleNavigation.cs` là nguồn duy nhất cho thanh điều hướng và kiểm thử. |
| Bảng vai trò → API | `tools/RestaurantManagement.DbTool/ApiAccessMatrix.cs` (101 API), khớp `[Authorize(Roles = …)]` trong code. Bảng đầy đủ ở docs/S1-04-Task4.md. |
| Quyền trong database | `031_RolePermissionsByScope.sql` bỏ `Menu.Availability`, `Kitchen.Read`, `Payments.Read` của Phục vụ, nên gọi thẳng thủ tục SQL cũng bị từ chối. Tổng hợp: docs/S1-04-TongHop.md. |

**Phạm vi của Phục vụ:**

| Màn hình (thanh điều hướng) | API được phép |
| --- | --- |
| **Sơ đồ bàn** (`/`) | `/Home/Index`, `/api/table-status` (xem, luồng cập nhật, đổi trạng thái bàn), `/api/table-map/{mã bàn}` |
| **Đặt bàn** (`/Reservations`) | Danh sách, chi tiết, tạo đặt bàn, kiểm tra lịch, bàn trống, trạng thái email. Huỷ và xoá vẫn chỉ Quản lý. |
| **Gọi món** (`/Ordering`) | `/Ordering`, `/Ordering/Checkout`, `/Orders/Details/{id}` |
| *(tài khoản của chính mình)* | Đổi mật khẩu, xác minh email, đăng xuất, `/api/account/me` |

## Gắn vai trò khi đăng nhập

- Máy chủ đọc `dbo.Roles.Code` của tài khoản và ghi vào claim `Role` của phiên. Trình duyệt không tự chọn vai trò được.
- **Đăng nhập bằng form:** vẫn chuyển trang như trước. Tên vai trò (“Phục vụ”) hiện cạnh tên tài khoản ở góc trái dưới.
- **Đăng nhập bằng fetch** (`POST /Account/Login` có header `X-Requested-With: XMLHttpRequest`): thành công trả JSON:
  ```json
  { "succeeded": true, "redirectUrl": "/",
    "user": { "userName": "waiter", "fullName": "…", "role": "Waiter", "roleName": "Phục vụ", "landingPath": "/",
              "navigation": [ { "key": "reservations", "title": "Đặt bàn", "path": "/Reservations" },
                              { "key": "table-map", "title": "Sơ đồ bàn", "path": "/" },
                              { "key": "ordering", "title": "Gọi món", "path": "/Ordering" } ] } }
  ```
  Sai thông tin trả `401` với `{ "succeeded": false, "message": "…" }`.
- **`GET /api/account/me`:** trả cùng thông tin `user` cho phiên đang đăng nhập.

## Thanh điều hướng

Phục vụ thấy **đúng 3 mục: Đặt bàn, Sơ đồ bàn, Gọi món**. Các mục *Thông tin*, *Quản lý món*, *Thực đơn* đã bỏ khỏi menu của Phục vụ.

## Chặn ở máy chủ

Phục vụ bị chặn ở các nhóm API sau, kể cả khi gõ thẳng đường dẫn hoặc gọi bằng fetch:

| Nhóm | API bị chặn |
| --- | --- |
| Thực đơn | Quản lý món `/Dishes` (danh sách, thêm, sửa, lịch sử giá), nhóm món `/DishCategories/*`, tạm hết `/Management/Availability` |
| Tài khoản | `/admin/employee-accounts` (danh sách, tạo, kiểm tra tên/số điện thoại, ngừng hoạt động) |
| Báo cáo | Nhật ký hệ thống `/AuditLogs`, hoá đơn `/Cashier/Invoices`, chốt ca `/Cashier/Shift` |
| Khác ngoài phạm vi | Màn hình bếp `/Kitchen`, trang *Thông tin*, khu vực/bàn/QR, giờ hoạt động, ngày nghỉ, thanh toán |

Thực đơn công khai cho khách (`/Menu`, `/api/menu`) vẫn mở cho mọi người, vì đó là trang của khách, không phải API quản lý thực đơn.

## Mã lỗi khi bị chặn (đã chốt với PO)

Ở giai đoạn này trả **dữ liệu lỗi thuần**, chưa có trang giao diện đẹp:

- **Gọi API** (`/api/...` hoặc fetch có `X-Requested-With`), sai vai trò: **403**, `Content-Type: application/json`:
  ```json
  {"status":403,"error":"forbidden","message":"Tài khoản của bạn không có quyền dùng chức năng này.","role":"Waiter","path":"/Dishes"}
  ```
- **Gọi API khi chưa đăng nhập:** **401** với `"error":"unauthorized"`.
- **Gõ thẳng đường dẫn trên trình duyệt:** từ S1-04 Task 3, trả **403** ngay tại đường dẫn đó kèm trang “Không có quyền truy cập” (xem docs/S1-04-Task3.md).

## Kiểm thử

| Bộ kiểm thử | Kiểm tra |
| --- | --- |
| `RoleNavigationTests` (AreaTests, không cần database) | Phục vụ thấy đúng 3 mục *Đặt bàn, Sơ đồ bàn, Gọi món*. Ngoài 3 nhóm đó và tài khoản của chính mình, Phục vụ không gọi được API nào. Các API thực đơn, tài khoản, báo cáo bị từ chối; API sơ đồ bàn, đặt bàn, gọi món được phép. Thông tin đăng nhập trả `Waiter`/“Phục vụ”. Lỗi quyền là JSON đúng định dạng. |
| `ApiAuthorizationCoverageTests` (AreaTests) | Quyền thật trong code (`[Authorize]`) khớp bảng phân quyền cho mọi vai trò. |
| `ApiAuthorizationVerification` (`verify-api-permissions`, web thật + SQL Server) | Phục vụ mở “/” thấy đúng 3 mục. Đăng nhập bằng fetch nhận vai trò `Waiter`; `/api/account/me` trả đúng 3 mục. 11 API thực đơn/tài khoản/báo cáo nhận **403 + JSON**. `/`, `/api/table-status`, `/Reservations`, `/Reservations/Create`, `/Ordering` trả **200**. Sau đó gọi toàn bộ 101 API cho từng vai trò. |
| `KitchenCashierVerification`, `SecurityAuditAccessVerification` | Phục vụ không mở được màn hình bếp và trang quản lý món. |

## Demo

```powershell
dotnet build RestaurantManagement.sln -m:1
dotnet run --project tests/RestaurantManagement.AreaTests --no-build
dotnet run --no-build --project tools/RestaurantManagement.DbTool -- verify-api-permissions
```

Trên trình duyệt:
1. Đăng nhập `waiter`: menu trái có đúng **Đặt bàn, Sơ đồ bàn, Gọi món**, góc dưới ghi “Phục vụ”.
2. Mở DevTools (F12) → Console, chạy:
   ```js
   for (const u of ['/Dishes', '/admin/employee-accounts', '/AuditLogs', '/api/table-status', '/Reservations', '/Ordering'])
     fetch(u, { headers: { 'X-Requested-With': 'XMLHttpRequest' } }).then(async r => console.log(u, r.status, (await r.text()).slice(0, 120)));
   fetch('/api/account/me').then(r => r.json()).then(console.log);
   ```
   Thực đơn, tài khoản, báo cáo trả `403` kèm JSON lỗi. Sơ đồ bàn, đặt bàn, gọi món trả `200`. `/api/account/me` trả `role: "Waiter"`.
3. Gõ thẳng `/Dishes`: nhận 403 và trang “Không có quyền truy cập” (S1-04 Task 3).
