# Task 1 — Trạng thái từng dòng món (AC1, AC3)

**Cập nhật Task 2:** phần không ghi thời điểm bên dưới mô tả phạm vi Task 1 ban đầu. Từ migration 044, chuyển trạng thái bếp đã ghi thời điểm và lịch sử; xem [Kitchen-Task2.md](Kitchen-Task2.md).

## Cách dùng

- Bếp đăng nhập, mở **Màn hình bếp** (`/Kitchen`).
- Tạo phiếu có ít nhất hai dòng bằng luồng Gọi món. Các dòng mới mặc định **Chờ bếp**.
- Bấm **Bắt đầu**, xác nhận: riêng dòng được chọn chuyển sang **Đang chế biến**, nhãn vàng.
- Bấm **Xong**, xác nhận: dòng chuyển sang **Đã xong**, nhãn xanh, rời hàng đợi chính và xuất hiện một lần ở **Chờ mang ra**, kèm bàn, phiếu, tên món và số lượng.
- Phục vụ đăng nhập ở trình duyệt khác, mở mục **Chờ mang ra** (`/Kitchen/Ready`). Trang tự lấy dữ liệu mỗi giây; bếp đọc lại ngay sau thao tác. Độ trễ thiết bị khác khoảng một giây cộng thời gian mạng, không cần tải lại trang.

## Quyết định cần PO xác nhận

Mặc định triển khai màn hình riêng cho phục vụ, kèm khu vực xem món xong ngay trên màn hình bếp. Món xong trước lên trước, dựa vào thứ tự cập nhật rowversion, không lưu thời điểm chuyển. Khi bấm có xác nhận ghi rõ món, bàn và trạng thái đích. Sau khi chuyển nhầm, báo quản lý/phục vụ phối hợp; không có chức năng chuyển lùi. Các lựa chọn này là đề xuất triển khai, chưa được PO xác nhận.

## Phạm vi và bảo vệ dữ liệu

Không có nút chuyển cả phiếu. Luồng Task 1 chỉ cập nhật Status và RowVersion; không ghi PreparingAt, ReadyAt hay sự kiện chuyển có thời điểm. Trường thời điểm và thủ tục nghiệp vụ cũ vẫn tồn tại cho các chức năng trước đó; API bếp hiện tại dùng thủ tục mới `usp_KitchenLineTransition` trong migration `043_KitchenLineWorkflow.sql`.

SQL chỉ nhận Pending → Preparing → Ready, kiểm tra quyền Kitchen.Manage và phiên chưa đóng. Điều kiện cập nhật gồm trạng thái nguồn và phiên bản dòng. Hai thiết bị bấm cùng phiên bản chỉ một yêu cầu thành công, yêu cầu còn lại nhận HTTP 409 kèm thông báo và tải trạng thái mới. Phục vụ chỉ đọc món Ready, không có quyền chuyển trạng thái. Đọc nền không gia hạn thời gian đăng nhập; lỗi mạng được báo và trang tiếp tục thử lại.

Thứ tự món xong dùng rowversion của dòng; thao tác khác sửa dòng Ready sẽ thay đổi thứ tự. Dữ liệu Ready từ luồng cũ không có bảo đảm thứ tự hoàn tất lịch sử.

## Kiểm tra

```powershell
node tools/tests/kitchen.test.cjs
dotnet build RestaurantManagement.sln --no-restore
dotnet run --no-build --project tests/RestaurantManagement.AreaTests
$env:RM_CONNECTION_STRING = 'Server=.\SQLEXPRESS;Database=RestaurantManagement_Dev;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True'
dotnet run --no-build --project tools/RestaurantManagement.DbTool -- verify-kitchen
```

`verify-kitchen` tạo database ngẫu nhiên riêng rồi xóa chính database đó. Bao phủ chuyển tuần tự, chặn chuyển lùi/nhảy cóc, hai yêu cầu cạnh tranh ở cả hai bước, dòng khác không đổi, không ghi thời điểm, phân loại món Ready đúng một lần, HTTP thật và quyền phục vụ. Kiểm tra JavaScript bao phủ nút theo trạng thái, cập nhật không nhân đôi, hủy xác nhận, báo xung đột, nội dung món an toàn và chế độ chỉ xem.
