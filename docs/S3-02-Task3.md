# S3-02 Task 3 — Chốt giá lúc gửi order

- Quy tắc: order luôn lấy giá hiện hành trên máy chủ. Nếu giá khách thấy trong giỏ đã cũ, hệ thống chưa tạo order; nó báo tên món và giá mới, giữ số lượng rồi cập nhật tạm tính.
- `OrderItems.UnitPrice` và `OrderItems.LineTotal` là ảnh chụp giá khi `usp_SubmitOrder` ghi nhận order. Sửa giá món sau đó không làm thay đổi các dòng đã gửi.
- Trang xác nhận hiển thị đơn giá chốt, thành tiền từng dòng và tổng tiền của order.
