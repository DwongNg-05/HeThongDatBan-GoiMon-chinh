# S1-04 Task 2: Bếp và Thu ngân chỉ thấy đúng phần việc của mình

**Mục tiêu:** Bếp và Thu ngân đăng nhập cũng chỉ thấy đúng phần việc của mình. Giống Phục vụ, mọi thứ được kiểm soát ở máy chủ. **Hoàn tất AC1 và AC2.**

Trang báo 403 khi gõ thẳng đường dẫn giao diện ngoài quyền: xem docs/S1-04-Task3.md. Quy tắc chung của cả 4 lát: docs/S1-04-TongHop.md.

> **Cập nhật:** màn hình “Món trong ngày” của Bếp (`/Kitchen/Dishes`, `/Kitchen/SoldOut`) đã bỏ vì trùng với **Quản lý món**. Báo món tạm hết / bán lại chỉ làm ở Quản lý món (`/Dishes`, Quản lý). Migration `032_RemoveKitchenDishes.sql` bỏ quyền `Menu.Availability` của Bếp trong database.

## Bảng ánh xạ vai trò → màn hình

Nguồn duy nhất là `Security/RoleNavigation.cs`. Thanh điều hướng (`_Layout.cshtml`) và các bài kiểm thử đều đọc từ đây.

| Vai trò | Thanh điều hướng | Mở “/” sau khi đăng nhập |
| --- | --- | --- |
| **Bếp** | **Màn hình bếp** (đúng 1 mục) | `/Kitchen` |
| **Thu ngân** | **Thanh toán**, **Hoá đơn**, **Chốt ca** (đúng 3 mục) | `/Cashier` |
| Phục vụ | Đặt bàn, Sơ đồ bàn, Gọi món (đúng 3 mục, S1-04 Task 1) | `/` (sơ đồ bàn) |
| Quản lý | Tất cả 15 mục, kể cả các màn hình của Bếp và Thu ngân | `/` |

**Vai trò được gắn khi đăng nhập:** máy chủ đọc `dbo.Roles.Code` của tài khoản (`kitchen` → `Kitchen`, `cashier` → `Cashier`) và ghi vào claim `Role` của phiên. Trình duyệt không tự chọn vai trò được.

## Màn hình mới

| Màn hình | Đường dẫn | Chức năng | Được phép (máy chủ) |
| --- | --- | --- | --- |
| Màn hình bếp | `GET /Kitchen` | Món chờ nấu / đang nấu / đã xong của các bàn đang phục vụ | Quản lý, Bếp (từ S1-04 Task 1, Phục vụ bị chặn) |
| | `POST /Kitchen/Advance/{id}` | Chờ nấu → Đang nấu → Xong (`usp_TransitionOrderItem`) | **Chỉ Bếp** (`Kitchen.Manage`) |
| Thanh toán | `GET /Cashier`, `POST /Cashier/Checkout` | Bàn chờ thanh toán; thu tiền mặt hoặc chuyển khoản (`usp_Checkout`, mỗi form một mã yêu cầu nên bấm 2 lần vẫn chỉ ra 1 hoá đơn) | Quản lý, Thu ngân (`Payments.Manage`) |
| Hoá đơn | `GET /Cashier/Invoices?date=` | Hoá đơn trong ngày và tổng đã thu | Quản lý, Thu ngân |
| Chốt ca | `GET /Cashier/Shift`, `POST /Cashier/OpenShift`, `POST /Cashier/CloseShift` | Mở ca; xem tiền mặt phải có; chốt ca (`usp_OpenShift`, `usp_CloseShift`, lệch quá 50.000 ₫ phải giải trình) | Quản lý, Thu ngân |

Mọi thao tác ghi dùng tài khoản đang đăng nhập làm `ActorUserId`. Thủ tục SQL kiểm tra quyền thêm một lần nữa. Quyền trong database được thu hẹp cho khớp web ở `031_RolePermissionsByScope.sql` (Bếp, Thu ngân không còn `Reservations.Read`).

## Chặn ở máy chủ API ngoài phạm vi

Ngoài các màn hình của mình, Bếp và Thu ngân chỉ còn:
- đổi mật khẩu, xác minh email, đăng xuất.

**Đặt bàn bị chặn hẳn:** Bếp và Thu ngân không xem được danh sách, chi tiết hay trạng thái email đặt bàn (`/Reservations`, `/Reservations/Details/{id}`, `/Reservations/EmailStatus/{id}`). Chỉ Quản lý và Phục vụ được xem.

Mọi API khác đều bị chặn với Bếp và Thu ngân: đặt bàn (xem, tạo, huỷ), sơ đồ bàn và API trạng thái bàn, quản lý món, gọi món, chi tiết order, khu vực/bàn/QR, giờ hoạt động, nhân viên, nhật ký. Bếp còn bị chặn thêm ở thanh toán, hoá đơn, chốt ca; Thu ngân bị chặn thêm ở màn hình bếp. Báo tạm hết (Quản lý món) chỉ dành cho Quản lý. Khi bị chặn:
- trang HTML trả 403 ngay tại đường dẫn đó, kèm trang “Không có quyền truy cập” (S1-04 Task 3);
- API trả mã 403.

Bảng đầy đủ: `docs/S1-04-Task4.md`.

## Kiểm thử

| Bộ kiểm thử | Kiểm tra |
| --- | --- |
| `RoleNavigationTests` (không cần database) | Bếp thấy đúng 1 mục, Thu ngân đúng 3 mục, Phục vụ và Quản lý đúng danh sách. Mỗi mục vai trò thấy đều được máy chủ cho phép với vai trò đó. Ngoài màn hình của mình và tài khoản, Bếp và Thu ngân không gọi được API nào. Chỉ Bếp chuyển được trạng thái chế biến (kể cả Quản lý cũng không). |
| `ApiAuthorizationCoverageTests` | Hai controller mới phải có trong bảng phân quyền với đúng vai trò. |
| `ApiAuthorizationVerification` (`verify-api-permissions`, web thật) | Đăng nhập lần lượt Phục vụ, Bếp, Thu ngân, Quản lý. Mở “/” phải vào đúng trang đầu; thanh điều hướng phải đúng các mục (Phục vụ 3, Bếp 1, Thu ngân 3). Sau đó gọi toàn bộ API: mọi API ngoài phạm vi đều bị chặn. |
| `KitchenCashierVerification` (`verify-api-permissions`, web thật) | Chạy một luồng thật: Thu ngân mở ca, Phục vụ gọi món, Bếp nấu xong, Thu ngân thanh toán và xem hoá đơn, Quản lý báo tạm hết rồi bán lại ở Quản lý món, Thu ngân chốt ca không lệch tiền. Ở mỗi bước còn kiểm tra vai trò khác bị chặn: Phục vụ/Thu ngân không đổi được trạng thái nấu, Bếp không thanh toán được, Bếp, Thu ngân, Phục vụ không báo tạm hết được (Bếp, Phục vụ bị database từ chối cả khi gọi thẳng thủ tục); `/Kitchen/Dishes` trả 404, Phục vụ không chốt ca được. |

## Demo

```powershell
dotnet build RestaurantManagement.sln -m:1
dotnet run --project tests/RestaurantManagement.AreaTests --no-build
dotnet run --no-build --project tools/RestaurantManagement.DbTool -- verify-api-permissions
```

Bằng tay, chạy web rồi đăng nhập lần lượt:
- **waiter:** thấy đúng 3 mục *Đặt bàn*, *Sơ đồ bàn*, *Gọi món* (chi tiết: docs/S1-04-Task1.md).
- **kitchen:** vào thẳng *Màn hình bếp*, thấy đúng *Màn hình bếp*. Gõ `/Areas` hoặc `/Cashier`: bị chặn (403).
- **cashier:** vào thẳng *Thanh toán*, thấy đúng *Thanh toán*, *Hoá đơn*, *Chốt ca*. Gõ `/Kitchen` hoặc `/Dishes`: bị chặn (403).
