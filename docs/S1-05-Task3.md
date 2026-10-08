# S1-05 Task 3 — Nhật ký chỉ đọc tuyệt đối, kiểm soát truy cập chặt chẽ

Mục tiêu: bảo đảm nhật ký chỉ đọc tuyệt đối và kiểm soát truy cập được củng cố. Hoàn tất **AC3** và **AC4**; story S1-05 không còn phần nào chưa xây dựng.

## Kết quả rà soát

Đã rà toàn bộ 129 file `.cs`/`.cshtml` của `RestaurantManagement.Web`, gồm controller, Razor Pages, view, service và `Program.cs`, cùng các migration và stored procedure.

| Hạng mục | Trước Task 3 | Sau Task 3 |
| --- | --- | --- |
| Màn hình **Nhật ký hệ thống** (`/AuditLogs`) | Chỉ một action GET, `[Authorize(Roles = "Manager")]`, không có nút sửa/xoá | Giữ nguyên. Thêm xử lý trường hợp quyền bị thu hồi trong phiên (xem dưới). Kiểm thử xác nhận bảng không có nút, liên kết hay ô nhập nào |
| Màn hình **Nhật ký thay đổi giá** (`/Dishes/PriceHistory/{id}`) | Mọi tài khoản đã đăng nhập đều xem được; nút “Lịch sử giá” hiện với mọi vai trò | Chỉ Quản lý. Nút chỉ hiện với Quản lý. POST/PUT/DELETE/PATCH luôn trả **405** |
| Đường gọi web trực tiếp (`POST/PUT/DELETE/PATCH /AuditLogs`, `/AuditLogs/Delete/1`, `/AuditLogs/Edit/1`, …) | Không có action ghi, nên đã trả 405/404 | Kiểm thử cho cả 3 vai trò không phải quản lý, người chưa đăng nhập và Quản lý: luôn bị từ chối, dữ liệu không đổi |
| Đường gọi qua EF (`ApplicationDbContext.AuditLogs`, `RestaurantDbContext.PriceChangeLogs`) | Về lý thuyết cho phép `Update`/`Remove` nếu có code gọi | `SaveChanges`/`SaveChangesAsync` ném lỗi khi có bản ghi nhật ký ở trạng thái sửa/xoá (`AppendOnlyGuard`); vẫn cho thêm |
| Database — chủ database | Trigger chặn UPDATE/DELETE trên `SecurityAuditLogs` và `AuditLogs`; **TRUNCATE vượt qua được trigger**; `MenuPriceHistory` chưa được bảo vệ | Thêm trigger chỉ-thêm cho `MenuPriceHistory`. Bảng chặn `AuditTruncateGuard` (luôn rỗng) có khoá ngoại tới cả 3 bảng, nên SQL Server từ chối `TRUNCATE` (lỗi 4712), kể cả với chủ database |
| Database — tài khoản ứng dụng (`restaurant_app`) | Không có `DENY` | `DENY SELECT/INSERT/UPDATE/DELETE/ALTER` trên `SecurityAuditLogs`; `DENY UPDATE/DELETE/ALTER` trên `AuditLogs` và `MenuPriceHistory`. `ALTER` bị từ chối nên TRUNCATE và `DISABLE TRIGGER` cũng bị chặn. Ứng dụng vẫn ghi/đọc qua stored procedure |
| Quyền bị thu hồi khi đang đăng nhập | Cookie vẫn ghi vai trò `Manager`; database từ chối (51001) nhưng web trả lỗi 500 | Bắt lỗi 51001 và trả **403** (từ S1-04 Task 3: trang “Không có quyền truy cập” ngay tại đường dẫn); không lộ dữ liệu |
| Các vai trò khác | Mới kiểm thử Phục vụ | Kiểm thử đủ **Phục vụ, Bếp, Thu ngân** (Thu ngân có quyền `Reports.Read` nhưng không có `Audit.Read`) và người chưa đăng nhập |

Migration mới: `022_AuditReadOnlyHardening.sql`. Các migration cũ không bị sửa.

**Lưu ý triển khai**: tài khoản Windows/`sysadmin` dùng khi phát triển vẫn có thể tắt trigger hoặc xoá bảng bằng quyền quản trị. Khi triển khai thật, website phải chạy bằng login riêng thuộc role `restaurant_app` (xem README, phần “Quy ước database”), không dùng tài khoản quản trị.

## Chạy demo

```powershell
cd D:\HeThongDatBan-Ordering-Chinh
$env:RM_CONNECTION_STRING = 'Server=.\MSSQLSERVER07;Database=RestaurantManagement_Dev;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True'
dotnet run --project tools/RestaurantManagement.DbTool -- migrate      # phải thấy "Applied: 022_AuditReadOnlyHardening.sql"
```

1. Đăng nhập bằng tài khoản **không phải Quản lý** (ví dụ `waiter`, `kitchen`, `cashier`): menu không có **Nhật ký hệ thống**; từ S1-04 Task 1/2, danh sách món (`/Dishes`) cũng chỉ dành cho Quản lý nên không thấy nút **Lịch sử giá**.
2. Gõ thẳng `/AuditLogs`, `/AuditLogs?fromDate=2026-09-01&toDate=2026-09-30&userId=1` hoặc `/Dishes/PriceHistory/1` lên thanh địa chỉ: đều bị chặn: mã 403 và trang “Không có quyền truy cập” (S1-04 Task 3).
3. Đăng xuất, đăng nhập bằng Quản lý và mở **Nhật ký hệ thống** và **Lịch sử giá**: chỉ có bộ lọc và bảng xem, không có nút sửa/xoá.
4. Tuỳ chọn, chạy trong SSMS để thấy database từ chối: `TRUNCATE TABLE dbo.SecurityAuditLogs;` báo lỗi 4712; `DELETE dbo.SecurityAuditLogs;` báo lỗi 51110.

## Kiểm thử

```powershell
dotnet build RestaurantManagement.sln --no-restore -m:1
dotnet run --project tests/RestaurantManagement.AreaTests --no-build
dotnet run --no-build --project tools/RestaurantManagement.DbTool -- verify
```

**`SecurityAuditAccessVerification`** chạy trong `verify`, trên SQL Server và qua HTTP thật:

- **Tài khoản không phải quản lý** (Phục vụ, Bếp, Thu ngân):
  - không thấy liên kết Nhật ký hệ thống và Lịch sử giá;
  - mở trực tiếp 7 đường dẫn đều nhận **403** kèm trang báo không có quyền ngay tại đường dẫn đó (S1-04 Task 3), không có dữ liệu nhật ký. Các đường dẫn gồm `/AuditLogs`, `/AuditLogs/Index`, `/auditlogs`, `/AUDITLOGS/INDEX`, `/AuditLogs/Index/1`, có kèm tham số lọc, và `/Dishes/PriceHistory/60`;
  - database cũng từ chối `usp_SecurityAuditList` và `usp_SecurityAuditAccounts` (51001).
- **Chưa đăng nhập**: mọi đường dẫn trên đều chuyển về trang đăng nhập.
- **Gọi sửa/xoá trực tiếp**: 9 đường dẫn × POST/PUT/DELETE/PATCH, gửi kèm token chống giả mạo hợp lệ, cho cả 3 vai trò không phải quản lý, người chưa đăng nhập và Quản lý. Mọi lời gọi đều bị từ chối (400/403/404/405 hoặc chuyển tới đăng nhập/AccessDenied).
- **Rà soát giao diện**: duyệt 18 trang với quyền Quản lý. Không có biểu mẫu POST nào trỏ tới nhật ký, không có liên kết tới action sửa/xoá nhật ký, không có chữ kiểu “xoá/sửa nhật ký”. Màn hình nhật ký chỉ có biểu mẫu lọc GET và nút đăng xuất; bảng không chứa nút, liên kết hay ô nhập.
- **Quyền bị thu hồi khi đang đăng nhập**: một tài khoản quản lý tạm bị đổi sang vai trò Phục vụ trong lúc đang đăng nhập. Lần mở tiếp theo bị chặn (403), dù cookie vẫn ghi `Manager`.
- **Gọi trực tiếp database** bằng một user thuộc role `restaurant_app`:
  - đọc thẳng bảng, chèn bản ghi giả, UPDATE, DELETE đều bị từ chối (lỗi 229); TRUNCATE và `DISABLE TRIGGER` cũng bị từ chối;
  - ghi và đọc qua stored procedure vẫn chạy được;
  - với chủ database: UPDATE nhật ký (51110), UPDATE/DELETE lịch sử giá (51112) và TRUNCATE cả 3 bảng (4712) đều bị chặn; bảng chặn không nhận dòng nào.
- **Dữ liệu cũ không đổi**: so dấu vân tay (số dòng + checksum) của nhật ký và lịch sử giá trước và sau mọi lần thử: không thay đổi.

**`AuditReadOnlyTests`** chạy trong AreaTests, không cần database:

- quét toàn bộ assembly web, không có hàm nào tên kiểu xoá/sửa/cập nhật nhật ký;
- controller nhật ký chỉ có 1 action GET, chỉ cho Quản lý;
- trang lịch sử giá chỉ cho Quản lý và trả 405 cho mọi phương thức ghi;
- kho nhật ký chỉ có `WriteLogin`, `Search`, `Accounts`;
- EF từ chối sửa/xoá bản ghi nhật ký và lịch sử giá nhưng vẫn cho thêm.

`AuthenticationHttpTests` bổ sung `/AuditLogs/Index` và `/Dishes/PriceHistory/1` vào danh sách bị chặn khi chưa đăng nhập.

Phần dọn dữ liệu của `MenuImageVerification` không còn xoá lịch sử giá, vì bảng này giờ chỉ cho thêm.
