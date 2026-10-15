# S4-05 Task 2 — thống kê trong khoảng tối đa 92 ngày

## Quy tắc đã được người yêu cầu xác nhận

- Giới hạn tính cả hai đầu: `ngày kết thúc - ngày bắt đầu + 1 <= 92`. Ví dụ 16/07/2026–15/10/2026 là 92 ngày; bắt đầu từ 15/07/2026 là 93 ngày và bị từ chối.
- Chọn cùng ngày bắt đầu/kết thúc: giữ biểu đồ theo giờ của Task 1. Chọn nhiều ngày: mỗi cột là một ngày kinh doanh, giữ ngày không có doanh thu bằng 0. Tổng các cột bằng doanh thu tổng.
- Bàn đang phục vụ luôn là số bàn vật lý trạng thái `Serving` hiện tại, không phải số bàn từng phục vụ trong khoảng lịch sử. Giao diện ghi rõ ngày giờ chụp số liệu.
- Doanh thu, số hóa đơn và lượt khách cộng trên các ngày hợp lệ; trung bình hóa đơn = tổng doanh thu / tổng số hóa đơn, không lấy trung bình của các trung bình từng ngày.
- Giữ quy tắc Task 1: giờ nhận thanh toán UTC+7, hóa đơn Paid có Payments, sau giảm giá, bỏ hóa đơn hủy. Chỉ tính giao dịch trong lịch mở cửa của từng ngày; đầu bao gồm, cuối loại trừ. Lịch đặc biệt/ngày nghỉ được áp dụng theo từng ngày.
- Ca qua nửa đêm thuộc ngày bắt đầu mở cửa. Nếu lịch hai ngày chồng nhau, giao dịch thuộc ngày kinh doanh bắt đầu trước để tránh đếm đôi. Giới hạn cấu hình giờ qua nửa đêm có sẵn của Task 1 vẫn còn; thay đổi này không sửa luồng đặt bàn/QR hay cấu hình giờ dùng chung.

## Cách dùng

Quản lý đăng nhập → **Thống kê doanh thu**, đường dẫn vẫn là `/Reports/Today`.

1. Mặc định hai ô ngày đều là ngày kinh doanh hiện tại theo UTC+7.
2. Chọn **Từ ngày**, **Đến ngày**, bấm **Xem thống kê**.
3. Nút **Hôm nay** đưa về mặc định.
4. Chọn nhiều ngày xem biểu đồ cột từng ngày; mở **Xem bảng doanh thu theo ngày** để đọc giá trị cụ thể.
5. Không có giao dịch: hiển thị **Chưa có dữ liệu**, chỉ số giao dịch bằng 0, ngày/giờ trống vẫn giữ trong biểu đồ.

Ví dụ hợp lệ: `/Reports/Today?from=2026-07-16&to=2026-10-15`.

Ví dụ vượt giới hạn: `/Reports/Today?from=2026-07-15&to=2026-10-15` → thông báo tối đa 92 ngày, HTTP 400, giữ ngày nhập để sửa và không hiển thị số liệu gây nhầm lẫn.

Máy chủ từ chối thiếu một hoặc cả hai ngày khi có bộ lọc, ngày không hợp lệ, đảo thứ tự và vượt 92 ngày trước khi truy cập SQL. Không có tham số ngày là yêu cầu mặc định hôm nay, khác với `?from=&to=`. Ngày hỗ trợ từ năm 1900 đến 9998. Lỗi tải dữ liệu trả HTTP 503, khác với khoảng không có giao dịch.

Không có cập nhật tự động. Dữ liệu đổi khi bấm Xem thống kê hoặc tải lại trang. Chỉ Quản lý được truy cập. Không cần migration hay dữ liệu mẫu mới.

## Kiểm thử

```powershell
dotnet run --project tests/RestaurantManagement.AreaTests -- --daily-overview-only
$env:RM_CONNECTION_STRING = 'Server=.\SQLEXPRESS;Database=RestaurantManagement_Dev;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True'
dotnet run --project tests/RestaurantManagement.AreaTests -- --daily-overview-db
```

Đã build thành công và đạt 37 kiểm tra quy tắc/controller/dựng Razor cùng 9 kiểm tra SQL chỉ đọc. Bao gồm 1/92/93 ngày, đảo thứ tự, thiếu ngày, ngày sai, hai đầu khoảng, ngày trống, khoảng trống, ca qua nửa đêm không đếm đôi, giữ biểu đồ giờ khi chọn một ngày, dựng biểu đồ ngày và thông báo lỗi đúng.

Database hiện chưa có hóa đơn: kiểm tra SQL xác nhận đường truy vấn và các tổng bằng 0. Kiểm tra giao dịch ở hai đầu và khoảng có doanh thu dùng dữ liệu đầu vào riêng trong bộ kiểm thử, không chèn hóa đơn giả vào database thật. Chưa kiểm tra trực quan trên trình duyệt. Bộ hồi quy toàn dự án có lỗi ma trận quyền `TableDetails.AddItemsQr GET` đã được ghi nhận ở Task 1; Task 2 không thêm endpoint mới.
