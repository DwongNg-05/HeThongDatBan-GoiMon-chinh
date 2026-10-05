# S2-03 Task 1 — Chặn trùng bàn và 15 phút dọn bàn

## Quy ước demo

- Quản lý là người tạo lượt đặt từ lịch bàn.
- Một lượt kéo dài theo `DefaultBookingMinutes` của nhà hàng (mặc định 90 phút).
- Mỗi lượt `Pending` hoặc `Confirmed` giữ bàn thêm 15 phút để dọn bàn.
- Lượt bắt đầu đúng mốc hết 15 phút dọn bàn được nhận.
- `Cancelled` và `NoShow` không giữ bàn. Hai trạng thái này được dùng đầy đủ ở Task 3.

## Cách demo

1. Đăng nhập bằng tài khoản Quản lý, mở **Lịch đặt bàn**.
2. Chọn ngày, bấm **Tạo lượt đặt bàn**, chọn một bàn và tạo lượt lúc `19:00`.
3. Tạo cùng bàn lúc `20:00` hoặc `20:40`: form giữ nguyên dữ liệu và báo *Bàn vừa có người đặt*.
4. Tạo cùng bàn lúc `20:45`: thành công vì đã qua 15 phút dọn bàn của lượt kết thúc `20:30`.
5. Tạo bàn khác lúc `19:00`: thành công; lịch hiển thị giờ kết thúc và mốc dọn bàn.

## Bảo vệ dữ liệu

Migration `028_S203TableReservationSchedule.sql` thêm trigger database. Vì vậy câu lệnh SQL ghi trực tiếp một lượt `Pending`/`Confirmed` giao nhau cũng bị từ chối, không chỉ biểu mẫu web.
