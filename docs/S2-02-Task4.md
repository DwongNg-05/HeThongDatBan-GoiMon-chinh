# S2-02 Task 4 - Khung giờ theo ngày và giờ mở cửa

Quy ước triển khai khi chưa có quyết định khác từ PO:

- Giờ mở cửa lấy từ cấu hình thứ trong tuần; ngày nghỉ đặc biệt đang hoạt động sẽ đóng cửa cả ngày.
- Khung giờ nhận khách cách nhau 30 phút, gồm giờ mở cửa và không gồm giờ đóng cửa.
- Khách có thể đặt hôm nay nếu khung giờ còn ở tương lai, và đặt trước tối đa 30 ngày.

Biểu mẫu gọi `GET /api/reservation-slots?date=yyyy-MM-dd` mỗi khi đổi ngày. API chỉ trả về khung giờ hợp lệ; ngày nghỉ hoặc ngày không còn khung sẽ có thông báo rõ ràng. Máy chủ và stored procedure kiểm tra lại cùng quy tắc để chặn yêu cầu gửi trực tiếp ngoài giờ.
