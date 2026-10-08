# Cảnh báo khách trễ và đánh dấu không tới

Quy tắc được chấp thuận: bắt đầu cảnh báo khi hiện tại **>= giờ hẹn + 15 phút**, chỉ với `Confirmed` và `ArrivedAt IS NULL`. Thời gian lưu UTC, hiển thị giờ Việt Nam. Số điện thoại bỏ khoảng trắng, dấu chấm, gạch ngang và ngoặc; `+84`/`0084` đổi thành `0`. Các lượt cùng số được gom chung lịch sử. Chưa triển khai gia hạn giữ bàn và cảnh báo ở lần đặt sau.

## Chạy và demo

1. Khởi động lại ứng dụng sau khi build; đăng nhập Manager hoặc Waiter.
2. Mở **Danh sách đơn** tại `/ReservationManagement` và **Sơ đồ bàn** tại `/TableMap` ở hai tab.
3. Chuẩn bị lượt **đã xác nhận**, đã xếp bàn, chưa đón khách. Khi đủ 15 phút trễ, mục **Khách trễ từ 15 phút** tự xuất hiện trong tối đa một chu kỳ 5 giây. Thẻ bàn có nhãn cảnh báo.
4. Bấm **Đánh dấu khách không tới**, xác nhận trong hộp thoại. Lượt chuyển sang **Khách không tới**, lịch sử có giờ hẹn và thời điểm ghi nhận. Bàn trống nếu không có lượt giữ khác đang áp dụng; không giải phóng bàn đang phục vụ.
5. Ở danh sách, chọn bộ lọc **Khách không tới** và bấm **Lịch sử không tới**; hoặc nhập số điện thoại vào ô **Lịch sử không tới theo số điện thoại**. Có thể nhập dạng `+84` hoặc `0084`.
6. Thử lại cùng lượt hoặc đón khách trước khi xác nhận không tới: hệ thống từ chối thao tác không còn hợp lệ; không tăng lịch sử và không giải phóng nhầm bàn.

Migration mới: `043_NoShowAlerts.sql`. Dùng DbTool `migrate` với `RM_CONNECTION_STRING` trỏ đúng database, không dùng EF để áp dụng tệp SQL này. Database trên máy hiện tại đã được sao lưu trước khi áp dụng. Không cần nhập lại dữ liệu đang có.

## Kiểm thử

```powershell
dotnet build tests/RestaurantManagement.AreaTests -p:UseAppHost=false -o .local/no-show-check -m:1
# Đặt RM_CONNECTION_STRING trỏ SQL Server thử nghiệm trước khi chạy.
dotnet .local/no-show-check/RestaurantManagement.AreaTests.dll --no-show
```

Bộ kiểm thử tạo database tên ngẫu nhiên `RestaurantNoShowTest_*`, áp dụng migrations rồi xóa chính database đó; không sửa dữ liệu nhà hàng.

Để chạy kiểm thử Chrome thật: cài/sử dụng Playwright cho Node, bảo đảm `require('playwright')` hoạt động (hoặc đặt `NODE_PATH` tới thư mục chứa module), rồi đặt `$env:NOSHOW_BROWSER_TEST='1'` trước lệnh test. Web kiểm thử dùng cổng ngẫu nhiên, tắt gửi email và tự dừng khi kết thúc.

Các kiểm thử bao gồm trước/đúng/sau ngưỡng, trạng thái không áp dụng, chuẩn hóa điện thoại, giải phóng bàn, trùng thao tác, hai nhân viên đồng thời, khách đã tới, rollback khi ghi lịch sử thất bại, quyền đọc của `restaurant_app`, chống CSRF và hai màn hình tự cập nhật không tải lại.

## Kết quả kiểm tra trên máy này

- 30 kiểm tra đã chạy đạt, gồm kiểm thử nghiệp vụ và Chrome thật không tải lại trang.
- Sau khi bổ sung các kiểm tra quyền SQL và rollback, Windows Application Control chặn lần chạy lại DLL DbTool mới. Hai nhóm kiểm tra bổ sung đã được chạy đạt trực tiếp bằng `sqlcmd -I` trên bản sao database tạm; bản sao được xóa sau kiểm thử. Không thay đổi chính sách bảo mật Windows.
- Build thành công; còn cảnh báo giấy phép ImageSharp có từ phần sửa trước.
- Migration 043 đã áp dụng trên `RestaurantManagement_Dev`; web mới khởi động và trang đăng nhập trả HTTP 200. Tiến trình web kiểm thử đã dừng, không chiếm cổng 7114.
- Bản sao lưu trước thay đổi: `.local/backups/RestaurantManagement_Dev_before_noshow_20261008.bak`.
