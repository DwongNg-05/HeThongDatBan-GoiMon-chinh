# S1-06 Task 4 — kiểm chứng theo yêu cầu

## Hành vi đã xác minh
| Yêu cầu | Triển khai | Kết quả |
| --- | --- | --- |
| Chỉ chọn khu vực đang sử dụng | ReservationsController.LoadActiveAreas lọc IsActive=1 trên GET và POST | Đạt qua HTTP |
| Đặt được tại khu vực đang dùng có bàn phù hợp | POST Create gọi usp_CreateReservation, lưu tên vào AreaNameSnapshot | Đạt qua HTTP |
| Chặn khu vực ngừng dùng | Kiểm tra lại lựa chọn ở controller, kiểm tra 51407 trong transaction SQL | Đạt qua HTTP và kiểm thử SQL đã có |
| Form mở trước khi ngừng dùng không được gửi thành công | POST đọc lại danh sách, trả lỗi tại PreferredAreaId và dropdown mới | Đạt qua HTTP với token từ form cũ |
| Giữ đặt bàn cũ | Deactivate chỉ cập nhật Areas.IsActive, không xóa/hủy đặt bàn | Đạt: số đặt bàn và số Pending không đổi |
| Giữ tên cũ ở danh sách và chi tiết | Dùng AreaNameSnapshot, không lọc IsActive khi đọc lịch sử | Đạt: cả 20 đặt bàn cũ và một đặt bàn mới trước lúc ngừng dùng |

## Kết quả chạy
- 15 unit checks đạt.
- Toàn bộ HTTP integration suite đạt, bao gồm ca gửi ID khu vực ngừng dùng và form cũ; không phát sinh đặt bàn mới khi bị từ chối.
- Kiểm tra chỉ đọc database ứng dụng: đã có cột snapshot, trigger lưu tên và guard 51407; không có đặt bàn có khu vực nhưng thiếu snapshot.
- Kiểm thử dùng database ngẫu nhiên và web riêng; đã dọn sau khi chạy. Không ngừng dùng/xóa khu vực hoặc đặt bàn thật trong lần kiểm tra này.

## Chạy lại
```powershell
$env:RM_CONNECTION_STRING = 'Server=(localdb)\MSSQLLocalDB;Database=RestaurantManagement_AreaReview;Integrated Security=true;TrustServerCertificate=true'
dotnet run --project tests/RestaurantManagement.AreaTests --artifacts-path artifacts/area-review -- --integration
```

## Demo trên ứng dụng
1. Chọn một khu vực đang sử dụng có bàn đủ chỗ, tạo ít nhất hai đặt bàn ở giờ hợp lệ.
2. Mở sẵn form đặt bàn mới ở tab khác và chọn khu vực đó.
3. Quản lý khu vực → Ngừng sử dụng → Xác nhận.
4. Mở form mới: khu vực không còn trong lựa chọn. Gửi form mở từ bước 2: bị từ chối.
5. Mở danh sách và chi tiết từng đặt bàn cũ: vẫn có tên khu vực lúc đặt và trạng thái đặt bàn không tự thay đổi.

Task 4 không yêu cầu hủy đặt bàn, xóa bàn hoặc tự chuyển bàn khi khu vực gặp sự cố. Việc xử lý các đặt bàn tương lai bị ảnh hưởng là nghiệp vụ riêng.

Giới hạn đã có: tên của dữ liệu trước migration 008 được backfill theo tên tại thời điểm nâng cấp; không thể phục hồi tên đã đổi trước đó nếu chưa từng được lưu. Tài khoản Manager hiện dùng cho demo localhost theo xác nhận của người dùng; đăng nhập thực là phạm vi riêng.

## Điều chỉnh theo yêu cầu mới
Migration 010 cho phép tạo yêu cầu Pending tại khu vực đang sử dụng dù chưa cấu hình bàn. Không tự cấp bàn hoặc xác nhận. Kiểm tra sức chứa, hoạt động và trùng lịch bàn vẫn nằm ở usp_ConfirmReservation. Quy tắc này thay thế yêu cầu phải có bàn phù hợp trước khi gửi đặt mới trong các tài liệu trước. Đã kiểm thử HTTP: khu vực trống nhận Pending với TableId NULL, lưu tên snapshot; sau ngừng dùng thì từ chối.

