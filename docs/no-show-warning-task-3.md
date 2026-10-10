# S3-09 Task 3 — Cảnh báo lịch sử không tới khi đặt bàn

## Quy tắc

- Cửa sổ cuốn chiếu 90 × 24 giờ theo UTC, từ `hiện tại - 90 ngày` đến `hiện tại`, gồm cả hai mốc. Dùng thời điểm ghi nhận `NoShowAt`, không dùng giờ hẹn. Không tính bản ghi tương lai.
- Dùng lịch sử bền vững `ReservationNoShowHistory`; xóa lượt đặt không xóa lịch sử này.
- Chuẩn hóa số điện thoại bằng quy tắc Task 1: bỏ khoảng trắng, dấu chấm, ngoặc, gạch ngang; đổi `+84`/`0084` thành `0`.
- Từ 3 lần trở lên vẫn được đặt, nhưng cần tích xác nhận đã đọc cảnh báo. Không tự động từ chối khách.
- Máy chủ kiểm tra lại số lần dưới cùng khóa giao dịch với thao tác tạo đặt bàn và ghi nhận không tới. Nếu số lần tăng hoặc xác nhận không hợp lệ/hết hạn, trả lại form với cảnh báo mới; không tạo lượt đặt hay gửi email ở lần bị chặn.
- Xác nhận được bảo vệ bằng ASP.NET Data Protection, gắn với số điện thoại chuẩn hóa và số lần đã xem, hiệu lực 1 giờ. Đổi số điện thoại sẽ bỏ xác nhận cũ.

## Kiểm tra trên web

1. Chạy lại ứng dụng trong Visual Studio.
2. Mở **Danh sách đặt bàn → tạo đặt bàn**, hoặc `/Reservations/Create`.
3. Nhập số có ít nhất 3 lần không tới trong 90 ngày: cảnh báo và số lần xuất hiện dưới ô điện thoại. Tích xác nhận để tiếp tục.
4. Đổi sang số dưới ngưỡng: cảnh báo biến mất. Thử các cách viết tương đương `0912345678`, `+84 912 345 678`, `0084-912-345-678` để kiểm tra chuẩn hóa.
5. Trong lúc đang tạo, một nhân viên khác ghi nhận thêm lần không tới: khi gửi form, số lần mới sẽ được kiểm tra và yêu cầu xác nhận lại nếu tăng.

Cùng chức năng được áp dụng cho `/PublicReservations/Create` và `/TableReservations/Create`. Không cần tải lại trang khi đổi số điện thoại. Khi không chạy JavaScript, máy chủ vẫn kiểm tra lúc gửi và hiển thị xác nhận.

## Database và kiểm thử

- Migration: `045_ReservationNoShowWarning.sql`. Dùng lại bảng lịch sử, thêm hàm đếm và cập nhật hai stored procedure tạo đặt bàn. Không sửa migration cũ.
- Đã sao lưu database phát triển trước khi áp dụng migration 045.
- Build đạt; có cảnh báo giấy phép ImageSharp sẵn có trong dự án.
- 40 kiểm tra Task 3 đạt, gồm Chrome thật ở ba form, kiểm tra gửi không qua tra cứu trước, số lần tăng 3 → 4 trước khi gửi, xác nhận lại, CSRF, token giả/sai số điện thoại, ngưỡng và hai biên thời gian.
- Hồi quy SQL: 31 kiểm tra Task 1 và 36 kiểm tra Task 2 đạt.
- Kiểm thử dùng database tạm riêng và xóa sau khi xong; không gửi email thật.

```powershell
dotnet build tests/RestaurantManagement.AreaTests --no-restore -p:UseAppHost=false -o .local/no-show-warning-check -m:1
# Cấu hình RM_CONNECTION_STRING trỏ SQL Server thử nghiệm có quyền tạo database.
# Với Chrome và module Node playwright có sẵn, bật kiểm thử trình duyệt:
$env:WARNING_BROWSER_TEST='1'
dotnet .local/no-show-warning-check/RestaurantManagement.AreaTests.dll --no-show-warning
```

Máy khác cần chạy DbTool migrate trước khi chạy phiên bản web này.
