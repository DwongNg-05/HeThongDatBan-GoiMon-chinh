# Task 2 – Từ chối và tra cứu lượt đặt

## Các lựa chọn tạm áp dụng, chờ PO xác nhận

- Khách dùng **mã đặt bàn + số điện thoại đã đặt**, không cần tài khoản. Đường dẫn `/Reservations`.
- Chỉ hiển thị lý do khi khách tự tra cứu; **không gửi email/SMS từ chối tự động**. Email xác nhận của Task 1 giữ nguyên.
- Ba mã cố định và câu hiển thị:

| Mã | Nhãn nhân viên | Câu cho khách |
|---|---|---|
| NoTable | Hết bàn | Nhà hàng đã hết bàn phù hợp trong khung giờ bạn chọn. |
| OutsideHours | Ngoài giờ phục vụ | Thời gian đặt bàn nằm ngoài giờ phục vụ. |
| Unreachable | Không liên lạc được | Nhà hàng không liên lạc được với bạn để xác nhận đặt bàn. |

## Cách dùng

1. Nhân viên Manager/Waiter mở **Xác nhận đặt bàn**, chọn một lượt Pending.
2. Mở **Từ chối lượt đặt**. Nút gửi bị khóa đến khi chọn một trong ba lý do.
3. Gửi: chuyển sang Rejected, TableId được xóa, quay lại danh sách chờ với thông báo thành công. Lượt vừa từ chối không còn trong danh sách chờ.
4. Khách mở **Tra cứu đặt bàn** tại `/Reservations`, nhập mã và số điện thoại. Kết quả hiển thị trạng thái và câu lý do tương ứng. Lượt Confirmed không hiển thị lý do từ chối.
5. Trang tạo đặt bàn thành công nay hiển thị mã đã sinh để khách lưu lại và có liên kết tra cứu.

Không bổ sung đổi bàn hay màn hình nhật ký. Giữ event nội bộ sẵn có để không phá các module khác. Từ chối chỉ cho Pending, kể cả giờ hẹn đã qua. Xác nhận và từ chối dùng chung transaction application lock; xử lý sau nhận lỗi và màn hình nạp trạng thái mới.

Tra cứu gửi POST, có CSRF, không cache, không trả tên khách/email/ghi chú nội bộ. Sai mã hoặc điện thoại trả cùng một thông báo. Giới hạn 20 lượt tra cứu/15 phút/IP, lưu vào LookupAttempts sẵn có. Dùng IP kết nối thực; cấu hình reverse proxy phải được xem xét riêng nếu triển khai sau proxy.

## Cập nhật và kiểm thử

Dùng connection string môi trường trong `RM_CONNECTION_STRING`, rồi chạy:

```powershell
dotnet run --project tools/RestaurantManagement.DbTool -- migrate
dotnet run --project tests/RestaurantManagement.AreaTests -- --rejection
```

Migration mới `033_S206ReservationRejection.sql` giữ nguyên dữ liệu hiện có; dùng lại trạng thái Rejected và cột RejectionReason đã có. Không sửa migration đã áp dụng.

Dữ liệu CF0001 của Task 1 có thể dùng demo nếu còn Pending: mã CF0001, số điện thoại 0900000099. Không tự từ chối hoặc sửa đặt bàn thực tế khi cài đặt.
