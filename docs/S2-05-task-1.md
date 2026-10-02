# S2-05 — Task 1: Danh sách đặt bàn theo ngày

Triển khai AC 1 và AC 2 tại `/Reservations`, dữ liệu tại `/Reservations/Daily?date=yyyy-MM-dd`.
Nhân viên đã đăng nhập có thể xem màn hình qua mục Đặt bàn.

## Quy ước đang áp dụng, chờ PO xác nhận

- Ngày mặc định là ngày hiện tại tại Asia/Ho_Chi_Minh (UTC+7), độc lập với múi giờ máy chủ và trình duyệt.
- Chọn lượt đặt có **giờ bắt đầu** nằm trong ngày đang xem; dùng khoảng UTC đầu ngày bao gồm, đầu ngày kế tiếp loại trừ.
- Khung giờ `HH:mm–HH:mm`; nếu qua đêm, ghi ngày kết thúc `HH:mm–HH:mm (dd/MM/yyyy)`.
- Form đặt bàn và SQL hiện hỗ trợ số điện thoại 10 chữ số. Giữ 3 số đầu và 3 số cuối, thay 4 số giữa bằng `****`, ví dụ `0901234123` thành `090****123`. Dữ liệu ngoài định dạng được che toàn bộ. API danh sách không trả số gốc.
- Sắp tăng dần theo giờ hẹn, cùng giờ thì theo ID tăng dần. Hiển thị toàn bộ trạng thái của ngày đó.

## Phạm vi Task 1

Hiển thị mã đặt bàn, tên khách, số điện thoại đã che, số khách, khung giờ, mã bàn và trạng thái tiếng Việt.
Lượt chưa có TableId hiển thị “Chưa xếp bàn”; khu vực mong muốn không được coi là bàn đã xếp.
Có chọn ngày, trở về hôm nay, trạng thái tải, thông báo trống và lỗi kèm Thử lại.
Yêu cầu mới huỷ yêu cầu đang tải trước đó để tránh kết quả ngày cũ ghi đè ngày mới.

Bộ lọc trạng thái và nhóm nổi bật trong 30 phút tới thuộc lát tiếp theo. Đề xuất cần PO xác nhận cho lát đó:
nhóm giờ bắt đầu trong khoảng `[hiện tại, hiện tại + 30 phút]` đứng đầu, từng nhóm sắp giờ hẹn tăng dần,
cùng giờ theo ID. Chưa áp dụng quy tắc nhóm này trong Task 1.

## Kiểm tra

- `dotnet run --project tests/RestaurantManagement.AreaTests -- --daily-reservations-sql`
  chạy kiểm tra C# và SQL trên một LocalDB tạm riêng, tự xoá sau khi chạy. Có thể đặt RM_CONNECTION_STRING cho SQL Server thử khác.
- `node --test tools/tests/daily-reservations.test.cjs`
  kiểm tra tải, đủ bảy cột, nội dung khách được đưa vào text an toàn, danh sách trống, lỗi và thử lại.
- Đăng nhập, mở `/Reservations`, kiểm tra ngày mặc định; chọn ngày có lượt đặt để kiểm tra danh sách và chọn ngày trống để kiểm tra thông báo.

Dữ liệu nhà hàng hiện tại không được thêm hoặc sửa bởi tính năng và các kiểm tra SQL.
