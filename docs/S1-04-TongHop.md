# S1-04: Phân quyền theo vai trò — tổng hợp Task 1–4

Tài liệu này gom 4 lát của S1-04 thành một quy tắc thống nhất. Chi tiết từng lát:

| Lát | Nội dung | Tài liệu |
| --- | --- | --- |
| Task 1 | Phục vụ chỉ thấy 3 màn hình; API ngoài phạm vi trả lỗi JSON | [S1-04-Task1.md](S1-04-Task1.md) |
| Task 2 | Bếp, Thu ngân chỉ thấy phần việc của mình | [S1-04-Task2.md](S1-04-Task2.md) |
| Task 3 | Gõ thẳng đường dẫn ngoài quyền → 403 + trang báo | [S1-04-Task3.md](S1-04-Task3.md) |
| Task 4 | Mọi API đều kiểm tra quyền ở máy chủ (101 API) | [S1-04-Task4.md](S1-04-Task4.md) |

## Phạm vi của từng vai trò

| Vai trò | Thanh điều hướng (mở “/” vào) | Được dùng | Bị chặn ở máy chủ |
| --- | --- | --- | --- |
| **Phục vụ** | Đặt bàn, Sơ đồ bàn, Gọi món (`/`) | Sơ đồ bàn và API trạng thái bàn; đặt bàn (xem, tạo); gọi món, chi tiết order | Thực đơn (quản lý món, nhóm món, báo tạm hết, món trong ngày); tài khoản nhân viên; báo cáo (nhật ký, hoá đơn, chốt ca); màn hình bếp; thông tin; khu vực, bàn, QR; giờ hoạt động, ngày nghỉ; huỷ/xoá đặt bàn |
| **Bếp** | Màn hình bếp (`/Kitchen`) | Xem món cần nấu, chuyển trạng thái chế biến (chỉ Bếp) | Đặt bàn (kể cả chỉ xem); sơ đồ bàn; gọi món; báo tạm hết (Quản lý món); thanh toán, hoá đơn, chốt ca; mọi màn hình quản trị |
| **Thu ngân** | Thanh toán, Hoá đơn, Chốt ca (`/Cashier`) | Thanh toán, hoá đơn trong ngày, mở/chốt ca | Đặt bàn (kể cả chỉ xem); sơ đồ bàn; gọi món; màn hình bếp; báo tạm hết; mọi màn hình quản trị |
| **Quản lý** | Cả 15 mục (`/`) | Toàn bộ, kể cả báo món tạm hết / bán lại ở Quản lý món | Chuyển trạng thái chế biến (việc của Bếp) |
| Vai trò không xác định | Không có mục nào | Chỉ đăng xuất | Mọi chức năng khác |

**Mọi vai trò đều dùng được:**
- đổi mật khẩu, xác minh email, đăng xuất;
- `GET /api/account/me`;
- thực đơn công khai cho khách (`/Menu`, `/api/menu`).

## Một nguồn cho mỗi quy tắc

| Quy tắc | Nằm ở | Kiểm tra bởi |
| --- | --- | --- |
| Tên vai trò, nhóm vai trò | `src/RestaurantManagement.Web/Security/AppRoles.cs` | `ApiAuthorizationCoverageTests` |
| Vai trò → màn hình (thanh điều hướng, trang đầu) | `Security/RoleNavigation.cs` | `RoleNavigationTests`, `verify-api-permissions` |
| Vai trò → API (101 API) | `[Authorize(Roles = …)]` trong code; bảng đối chiếu `tools/RestaurantManagement.DbTool/ApiAccessMatrix.cs` | `ApiAuthorizationCoverageTests` (phản chiếu), `verify-api-permissions` (HTTP thật) |
| Quyền trong database | `dbo.RolePermissions` (`005_ReferenceData.sql`, thu hẹp ở **`031_RolePermissionsByScope.sql`** và **`032_RemoveKitchenDishes.sql`**) | `verify-api-permissions`, `KitchenCashierVerification` |

## Khi bị chặn: máy chủ trả gì

| Trường hợp | Mở trang trên trình duyệt | Gọi API (`/api/...` hoặc fetch có `X-Requested-With`) |
| --- | --- | --- |
| Chưa đăng nhập | Chuyển tới `/Account/Login?ReturnUrl=…` | **401** + JSON `{"status":401,"error":"unauthorized",…}` |
| Đăng nhập nhưng sai vai trò | **403 ngay tại đường dẫn đã gõ** + trang “Không có quyền truy cập” (Task 3) | **403** + JSON `{"status":403,"error":"forbidden","message":…,"role":…,"path":…}` (Task 1) |
| Vai trò không xác định | 403 + trang báo, chỉ có nút Đăng xuất | 403 + JSON |
| Chưa xác minh email / phải đổi mật khẩu | Chuyển tới màn hình xác minh / đổi mật khẩu trước khi xét quyền | Như cột bên trái |

Thanh điều hướng chỉ là phần hiển thị. Máy chủ chặn từng API theo vai trò. Thủ tục SQL kiểm tra quyền thêm một lần nữa, nên gọi thẳng database cũng bị từ chối.

## Quyết định cần PO xác nhận

1. **Lỗi khi gọi API bị chặn** (Task 1): trả dữ liệu lỗi JSON thuần, chưa có giao diện.
2. **Trang báo không có quyền** (Task 3):
   - thông điệp “Tài khoản của bạn không có quyền mở màn hình này.”;
   - **có** nút “Về màn hình chính”, trỏ về trang đầu của vai trò;
   - kèm danh sách màn hình được dùng.
   - Câu chữ nằm trong `Models/Account/AccessDeniedViewModel.cs`.
3. **Phục vụ không xem màn hình bếp** (Task 1): màn hình bếp không thuộc 3 phần việc của Phục vụ. Nếu PO muốn Phục vụ theo dõi món đã nấu xong, cần mở lại `Kitchen.Index` cho Phục vụ (AppRoles + ApiAccessMatrix + 031).
4. **Bếp, Thu ngân bị chặn hẳn ở đặt bàn** (Task 2, đã chọn “Chặn hẳn”).

## Bỏ màn hình “Món trong ngày”

Màn hình “Món trong ngày” của Bếp (`/Kitchen/Dishes`, `/Kitchen/SoldOut`) đã bỏ vì trùng với **Quản lý món**. Báo món tạm hết / bán lại chỉ làm ở Quản lý món (`/Dishes`, Quản lý). Migration `032_RemoveKitchenDishes.sql` bỏ quyền `Menu.Availability` của Bếp trong database.

## Những gì đã thống nhất lại

Rà soát 4 lát sau khi làm xong, các chỗ lệch đã được sửa:

| Chỗ lệch | Đã sửa |
| --- | --- |
| Web chặn chặt hơn database: Phục vụ vẫn có `Menu.Availability`, `Kitchen.Read`, `Payments.Read`; Bếp, Thu ngân vẫn có `Reservations.Read`. Gọi thẳng `usp_SetMenuAvailability` bằng tài khoản Phục vụ vẫn được. | Thêm migration `031_RolePermissionsByScope.sql`. `verify-api-permissions` kiểm tra database không còn quyền thừa. `KitchenCashierVerification` kiểm tra Phục vụ bị chặn báo tạm hết ở cả web lẫn database. |
| Ô kiểm tra lịch đặt bàn, danh sách bàn trống, kiểm tra mã bàn gọi fetch **không** kèm `X-Requested-With`. Khi bị chặn hoặc hết phiên, các ô này nhận trang HTML thay vì lỗi JSON như quy tắc Task 1. | Ba tệp `booking-schedule.js`, `booking-tables.js`, `table-form.js` gửi kèm header. `table-form.js` báo lỗi khi máy chủ từ chối thay vì đọc sai dữ liệu. |
| Chú thích và tài liệu cũ còn ghi “4 vai trò đều xem đặt bàn”, “chuyển tới /Account/AccessDenied”, “Phục vụ 6 mục”. | Cập nhật `AppRoles.cs`, `AuditLogsController.cs`, `BookingConfirmationEmailTests.cs`, tài liệu S1-04 Task 1–4, S1-05 Task 1/3 và README. |
| `?? []` (collection expression sau `??`) trong `BookingConfirmationEmailTests.cs` có thể không biên dịch, mà dự án bật `TreatWarningsAsErrors`. | Đổi sang `Array.Empty<string>()`. |

## Kiểm thử và demo

```powershell
dotnet build RestaurantManagement.sln -m:1
dotnet run --project tools/RestaurantManagement.DbTool -- migrate
dotnet run --project tests/RestaurantManagement.AreaTests --no-build
dotnet run --no-build --project tools/RestaurantManagement.DbTool -- verify-api-permissions
```

`migrate` áp dụng `031_RolePermissionsByScope.sql` và `032_RemoveKitchenDishes.sql` cho database đang dùng. `verify-api-permissions` tự tạo database tạm nên đã có sẵn 031.

Kết quả mong đợi gồm các dòng `PASS`:
- `S1-04 — quyền trong database khớp…`;
- `S1-04 Task 2 — <vai trò> mở "/" vào …` (Phục vụ 3 mục, Bếp 1, Thu ngân 3);
- `S1-04 Task 4 — <vai trò>: 101 API…`;
- `S1-04 Task 1 — Phục vụ: …`;
- `S1-04 Task 3 — <vai trò>: … màn hình ngoài quyền trả 403…`;
- luồng Bếp/Thu ngân `S1-04 Task 2 kitchen and cashier work flow checks`.

Demo bằng tay: đăng nhập lần lượt `waiter`, `kitchen`, `cashier`, rồi:
1. xem menu trái;
2. gõ thẳng một đường dẫn ngoài quyền (ví dụ `waiter` gõ `/Dishes`, `kitchen` gõ `/Reservations`, `cashier` gõ `/Kitchen`) → thấy trang “Không có quyền truy cập”, mã 403 (F12 → Network);
3. bấm “Về màn hình chính” để quay về.
