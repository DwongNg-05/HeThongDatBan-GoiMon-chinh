# S1-10 Task 6 — Luồng quản lý tập trung

Mở `/OpeningHours`: lịch 7 ngày, thời lượng giữ bàn, khung giờ xem trước, toàn bộ ngày nghỉ đặc biệt và kiểm tra lịch ngày cụ thể nằm trên cùng màn hình. Thêm/sửa/xóa ngày nghỉ được tải trong phần ngày nghỉ, giữ nguyên giờ tuần đang nhập chưa lưu. Khi JavaScript không khả dụng, liên kết và form vẫn hoạt động bằng các trang hiện có.

Lịch tuần và thời lượng có nút lưu riêng. Ngày nghỉ có nút lưu riêng trong form thêm/sửa; lưu lịch tuần không cập nhật hay xóa ngày nghỉ. Khi còn chỉnh sửa tuần chưa lưu, màn hình báo rõ và cảnh báo trước khi rời trang. Xem lịch theo ngày và thử đặt bàn dùng cấu hình đã lưu, còn khung giờ từng thứ là xem trước biểu mẫu hiện tại. Sau thay đổi ngày nghỉ, phần xem lịch nhắc tải lại kết quả. Thử đặt bàn mở tab mới để giữ màn hình cấu hình.

Giờ đóng không sau giờ mở và thời lượng ngoài 30–360 phút có lỗi tại trường ngay khi nhập; máy chủ vẫn kiểm tra và trả lỗi tại trường. Ngày nghỉ tuần không có lượt; ngày nghỉ đặc biệt hiển thị ngày/tên/trạng thái, được ưu tiên trong lịch ngày cụ thể.

## Kịch bản nghiệm thu

1. Mở màn hình: đủ 7 ngày, thời lượng mặc định 90 nếu chưa thay đổi.
2. Đổi giờ thành 08:15–22:00, xem lượt 08:15, 08:45…21:45; đổi thời lượng 150, chọn Chủ nhật nghỉ và lưu.
3. Thêm ngày nghỉ đặc biệt, sửa tên hoặc trạng thái ngay trong phần ngày nghỉ. Thử trùng ngày để thấy lỗi tại ô ngày.
4. Sửa giờ tuần lần nữa rồi lưu: các ngày nghỉ đã khai báo vẫn còn nguyên.
5. Chọn ngày nghỉ tại phần kiểm tra lịch: không có khung giờ; chọn ngày làm việc: có lượt theo cấu hình.
6. Mở Thử đặt bàn: giờ đúng lượt được tiếp tục; ngoài giờ, ngày nghỉ tuần hoặc ngày lễ bị chặn. Giờ dùng Asia/Ho_Chi_Minh.
7. Tải lại màn hình quản lý: giờ tuần, thời lượng và danh sách ngày nghỉ giữ nguyên.

Build và chạy `--opening-hours-http` theo README để kiểm tra xuyên suốt Task 1–6 bằng database tạm. Không cần migration mới cho Task 6. Web vẫn dùng danh tính quản lý demo phía máy chủ của dự án; story đăng nhập không nằm trong thay đổi này.
