# S1-04 Task 3: Trang báo không có quyền truy cập (403)

**Mục tiêu:** gõ thẳng đường dẫn một màn hình ngoài quyền thì nhận mã lỗi và trang báo rõ ràng, thay vì một lỗi khó hiểu. **Hoàn tất AC3.**

## Trước và sau

| | Trước | Sau |
| --- | --- | --- |
| Phục vụ gõ `/Dishes` | 302 chuyển sang `/Account/AccessDenied`, trang đó trả 403 với một dòng chữ thuần | **403 ngay tại `/Dishes`**, địa chỉ trên trình duyệt giữ nguyên, hiện trang “Không có quyền truy cập” |
| Gọi API (`/api/...` hoặc fetch có `X-Requested-With`) | 403 + JSON (S1-04 Task 1) | Không đổi |
| Chưa đăng nhập | Chuyển tới trang đăng nhập | Không đổi |

## Cách hoạt động

1. **Bắt truy cập ngoài quyền.** `[Authorize(Roles = …)]` trên controller hoặc Razor Page từ chối vai trò đang đăng nhập. Sự kiện cookie `IdleSessionEvents.RedirectToAccessDenied` nhận yêu cầu đó.
2. **Trả 403 kèm trang báo.**
   - Nếu là màn hình giao diện, `AccessDeniedPage.TryShow` đặt mã **403** và bật trang mã lỗi cho riêng yêu cầu này.
   - `UseStatusCodePagesWithReExecute("/Account/AccessDenied")` hiển thị trang báo ngay trong phản hồi đó, không chuyển hướng.
   - Các mã lỗi khác (404, 400 do thiếu mã chống giả mạo…) không bị ảnh hưởng, vì trang mã lỗi mặc định bị tắt (`AccessDeniedPage.DisableByDefault`).
3. **Mở trực tiếp `/Account/AccessDenied`:** vẫn hiện cùng trang, mã 403.

## Nội dung trang (đã chốt với PO)

| Thành phần | Nội dung |
| --- | --- |
| Mã lỗi | “Lỗi 403” |
| Tiêu đề | **Không có quyền truy cập** |
| Thông điệp | “Tài khoản của bạn không có quyền mở màn hình này.” |
| Ngữ cảnh | Tên đăng nhập và vai trò (ví dụ “waiter”, “Phục vụ”); đường dẫn đã mở (ví dụ `/Dishes`) |
| Nút về màn hình chính | **Có.** “Về màn hình chính (…)” trỏ tới trang đầu của vai trò: Quản lý, Phục vụ → *Sơ đồ bàn* (`/`); Bếp → *Màn hình bếp* (`/Kitchen`); Thu ngân → *Thanh toán* (`/Cashier`) |
| Màn hình được dùng | Danh sách liên kết tới các màn hình của vai trò (giống thanh điều hướng) |
| Hướng dẫn | “Nếu bạn cần dùng chức năng này, hãy liên hệ Quản lý để được cấp quyền.” |
| Vai trò không xác định | Không có màn hình nào để quay về, nên chỉ có nút **Đăng xuất** |

Trang dùng chung bố cục hệ thống, nên thanh điều hướng của vai trò vẫn hiện bên trái. Trang không chứa dữ liệu của màn hình bị chặn.

**Mã nguồn:**
- `Authentication/AccessDeniedPage.cs` (mới);
- `Authentication/IdleSessionEvents.cs`;
- `Program.cs`;
- `Controllers/Account/AccountController.cs` (`AccessDenied`);
- `Models/Account/AccessDeniedViewModel.cs` (mới);
- `Views/Account/AccessDenied.cshtml`;
- `wwwroot/css/admin-shell.css`.

## Kiểm thử

| Bộ kiểm thử | Kiểm tra |
| --- | --- |
| `RoleNavigationTests` (AreaTests, không cần database) | Nút về màn hình chính đúng trang đầu của từng vai trò. Danh sách màn hình trên trang báo trùng thanh điều hướng. Vai trò không xác định chỉ có đăng xuất. Danh sách màn hình trong kiểm thử HTTP khớp thanh điều hướng. |
| `ApiAuthorizationVerification` (`verify-api-permissions`, web thật + SQL Server) | Đăng nhập lần lượt Quản lý, Phục vụ, Bếp, Thu ngân và một vai trò không xác định. Mỗi vai trò gõ thẳng 14 màn hình trên thanh điều hướng (trừ thực đơn công khai) và 6 màn hình tạo/cấu hình. **Ngoài quyền:** 403, không chuyển hướng, có tiêu đề, thông điệp, đường dẫn đã mở và nút về đúng trang đầu (vai trò không xác định: nút đăng xuất). **Trong quyền:** 200 bình thường. Bếp, Thu ngân gõ `/` được đưa về trang của mình. |
| `SecurityAuditVerification`, `SecurityAuditAccessVerification`, `BookingConfirmationVerification` | Đã cập nhật sang 403 + trang báo: nhật ký, lịch sử giá, quản lý món, đặt bàn khi vai trò không có quyền, Quản lý bị hạ vai trò giữa phiên. Trang báo không lộ dữ liệu nhật ký hay mã bàn. |
| `KitchenCashierVerification` | Bước bị chặn nhận 403 (đã chấp nhận từ trước). |

## Demo

```powershell
dotnet build RestaurantManagement.sln -m:1
dotnet run --project tests/RestaurantManagement.AreaTests --no-build
dotnet run --no-build --project tools/RestaurantManagement.DbTool -- verify-api-permissions
```

Trên trình duyệt:
1. Đăng nhập `waiter`, gõ thẳng `/Dishes` (màn hình thực đơn). Thấy trang **Không có quyền truy cập**, địa chỉ vẫn là `/Dishes`. Ở F12 → Network, dòng `Dishes` có mã **403**.
2. Bấm **Về màn hình chính (Sơ đồ bàn)** để quay về sơ đồ bàn.
3. Gõ `/Reservations` hoặc `/Ordering` (thuộc quyền): vào bình thường.

Ngoài phạm vi task này: chưa rà soát toàn diện các API còn lại ngoài những API đã xử lý ở hai lát trước (phần này do S1-04 Task 4 đảm nhận). Quy tắc chung của cả 4 lát: docs/S1-04-TongHop.md.
