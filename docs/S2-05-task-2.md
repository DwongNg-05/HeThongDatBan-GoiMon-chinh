# S2-05 — Task 2: Lọc trạng thái đặt bàn

Phạm vi: AC 3, giữ nguyên AC 1 và AC 2. Không triển khai nhóm nổi bật trong 30 phút tới.

Quy ước đã được người dùng chốt: chọn một trạng thái mỗi lần, mặc định Tất cả trạng thái.
Các lựa chọn: Chờ xác nhận (`Pending`), Đã xác nhận (`Confirmed`), Đã huỷ (`Cancelled`), Khách không tới (`NoShow`).
Tất cả trạng thái bao gồm cả Đã từ chối và Khách đã tới của hệ thống.

API `/Reservations/Daily?date=yyyy-MM-dd&status=Pending` lọc trên SQL bằng tham số,
giữ nguyên điều kiện ngày Việt Nam và giờ hẹn tăng dần. Không truyền status hoặc status rỗng để bỏ lọc.
Trạng thái không hỗ trợ trả HTTP 400. Không thay đổi dữ liệu đặt bàn.

Đổi trạng thái cập nhật danh sách của ngày đang xem ngay; Bỏ lọc khôi phục Tất cả trạng thái.
Thử lại giữ ngày và trạng thái đã yêu cầu. Yêu cầu cũ bị huỷ khi đổi lựa chọn, kết quả cũ không ghi đè kết quả mới.
Kết quả rỗng khi lọc hiển thị “Không có lượt đặt bàn phù hợp với ngày và trạng thái đã chọn.”

Kiểm tra: từng trạng thái, bỏ lọc, dữ liệu khác ngày, thứ tự tăng dần, đủ thông tin và che số điện thoại,
kết quả lọc rỗng, lỗi/thử lại và đổi bộ lọc liên tiếp.

Demo: đăng nhập → Đặt bàn → chọn trạng thái → kiểm tra các lượt phù hợp → Bỏ lọc.
Mặc định vẫn là hôm nay (UTC+7), có thể chọn ngày khác bằng chức năng Task 1.
