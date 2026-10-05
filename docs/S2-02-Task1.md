# S2-02 Task 1 — Đặt bàn cơ bản với mã đặt bàn

Khách đặt bàn công khai tại `/Reservations/Create`, không cần tài khoản. Biểu mẫu gồm họ tên, số điện thoại, số khách, ngày, khung giờ, khu vực và ghi chú. Hệ thống trả mã sáu ký tự, lưu đơn `Pending`, và hiển thị tóm tắt.

- Khu vực demo: Trong nhà, Ngoài trời, Phòng riêng hoặc Không yêu cầu.
- Mã dùng chữ hoa/số, không dùng `0`, `O`, `1`, `I`, duy nhất vĩnh viễn.
- Nhân viên xem danh sách và ghi chú tại `/ReservationManagement`.

Chạy `dotnet run --project tools/RestaurantManagement.DbTool -- migrate` trước khi khởi động web để áp dụng migration `024_BasicPublicReservation.sql`.
