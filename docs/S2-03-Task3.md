# S2-03 Task 3 — Huỷ hoặc khách không tới

- Chỉ Quản lý có thể huỷ hoặc đánh dấu khách không tới.
- Có hộp xác nhận trước khi đổi trạng thái.
- Chỉ lượt `Pending`/`Confirmed` được đổi. Huỷ lặp lại hoặc đánh dấu lại trả thông báo rõ ràng và không sửa dữ liệu.
- `NoShow` chỉ được đánh dấu sau giờ bắt đầu của lượt.
- Lượt `Cancelled` và `NoShow` vẫn hiển thị trong phần **Đã giải phóng** để tra cứu, nhưng không giữ bàn và không xuất hiện trong phần chiếm chỗ/gợi ý giờ.
