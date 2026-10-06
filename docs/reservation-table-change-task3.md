# Task 3 – Đổi bàn và nhật ký

## Quy tắc đã được người dùng chốt

- Chỉ đổi lượt Confirmed khi hiện tại **nhỏ hơn** StartsAt. Đúng giờ hẹn hoặc muộn hơn bị chặn, không có mốc chốt trước 30 phút.
- Bàn mới khác bàn cũ, bàn và khu vực đang sử dụng, MaxCapacity >= GuestCount, không có Pending/Confirmed trùng khoảng (kể cả 15 phút dọn bàn). Khu vực mong muốn được ưu tiên trong danh sách.
- Không gửi thông báo tự động cho khách khi đổi bàn.
- Lý do không bắt buộc, tối đa 500 ký tự. Manager và Waiter được đổi và xem lịch sử.
- Mỗi lần thành công ghi người đổi (ID và tên tài khoản tại thời điểm đổi), thời điểm UTC, ID/mã bàn cũ và mới, lý do. Mã bàn và tên người đổi được chụp lại để lịch sử không thay đổi khi danh mục đổi tên. Giao diện hiển thị giờ Việt Nam, mới nhất trước; Id phân thứ tự khi thời gian bằng nhau.

## Cách dùng

Từ **Đặt bàn → Chi tiết → Xử lý đặt bàn và lịch sử đổi bàn**, hoặc từ **Xác nhận đặt bàn → Lịch bàn đã đặt trước → mã đặt**:

1. Mở lượt đã xác nhận trước giờ hẹn.
2. Mở phần **Đổi bàn**, chọn bàn thay thế, nhập lý do nếu cần.
3. Bấm **Xác nhận đổi bàn**. Chi tiết hiển thị bàn mới và thêm một dòng lịch sử.
4. Bàn cũ được giải phóng cho đúng khung giờ, lượt khác có thể xác nhận vào bàn đó. Những khung giờ khác của bàn không bị thay đổi.
5. Lượt quá giờ hẹn không có form đổi bàn; POST trực tiếp vẫn bị SQL chặn.

Giữ nguyên cách hiển thị trạng thái vận hành của Task 1: bản đồ hiện Reserved khi lượt đặt trong cửa sổ 30 phút tới. Các lượt xa hơn được giữ trong lịch đặt bàn theo khung giờ. Đổi bàn tính lại trạng thái Available/Reserved của cả hai bàn nhưng không ghi đè Serving/Cleaning.

Transaction dùng chung khóa với xác nhận/từ chối. Kiểm tra lại bàn trống, sức chứa, thời gian và bàn cũ tại lúc gửi. Nếu người khác đã đổi bàn của cùng lượt, form cũ bị từ chối và nạp lại thông tin. Thay đổi lượt đặt, trạng thái bàn và nhật ký commit/rollback cùng nhau. Nhật ký không có thao tác sửa/xóa.

## Cài đặt và kiểm thử

Migration: `034_S206ReservationTableChanges.sql`. Không sửa các migration cũ.

```powershell
# RM_CONNECTION_STRING trỏ tới SQL Server phát triển
dotnet run --project tools/RestaurantManagement.DbTool -- migrate
dotnet run --project tests/RestaurantManagement.AreaTests -- --confirmation
```

Lệnh kiểm thử chạy các ca Task 1, 2, 3 trên database tạm tự dọn, gồm kiểm thử SQL, HTTP, CSRF, vai trò, lỗi form cũ, hết giờ, đổi liên tiếp và tranh chấp. Không tự đổi bàn của khách thật để demo. Build xong, chạy F5 từ Visual Studio; không chạy thêm một server cùng cổng ở terminal.
