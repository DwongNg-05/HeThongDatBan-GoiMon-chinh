# S4-05 Task 1 — tổng quan hôm nay

## Quy tắc được người yêu cầu xác nhận

- Múi giờ UTC+7. Không dùng múi giờ trình duyệt hoặc máy chủ.
- Doanh thu: tổng `Invoices.Total` (sau giảm giá), chỉ hóa đơn `Paid` có bản ghi `Payments`. Hóa đơn `Voided` bị loại khỏi mọi chỉ số.
- Thời điểm ghi nhận: `Payments.ReceivedAt`, không phải giờ bắt đầu phục vụ hay ngày mở ca. Không dùng tiền khách đưa trước khi trả lại tiền thừa.
- Số hóa đơn: số hóa đơn thỏa điều kiện trên; lượt khách: tổng `Invoices.GuestCount` trên cùng tập hóa đơn. Không phải số người duy nhất.
- Trung bình mỗi hóa đơn: doanh thu / số hóa đơn; không có hóa đơn thì bằng 0. Số tiền hiển thị làm tròn đến đồng, tính toán dùng decimal.
- Bàn đang phục vụ: số bàn vật lý trạng thái `Serving` tại lúc tải trang, kể cả khi chưa có hóa đơn trong ngày.
- Khoảng hoạt động `[mở cửa, đóng cửa)`. Khung 1 tiếng bắt đầu từ giờ mở cửa; khung cuối có thể ngắn hơn 1 tiếng. Ví dụ 09:30–12:00 có các khung 09:30–10:30, 10:30–11:30, 11:30–12:00. Mọi khung trống vẫn có giá trị 0.
- Với lịch qua nửa đêm, đóng cửa nhỏ hơn hoặc bằng mở cửa được diễn giải ở ngày hôm sau. Trước giờ đóng cửa, mặc định tiếp tục ngày kinh doanh hôm trước; đúng lúc đóng cửa chuyển sang ngày lịch hiện tại. Giao dịch đúng giờ đóng bị loại, đúng đầu khung thuộc khung mới.
- Lịch đặc biệt `SpecialDates` ưu tiên lịch tuần; ngày nghỉ `SpecialHolidays` đang bật đóng cửa. Thiếu lịch được xử lý như đóng cửa, có thông báo.

## Sử dụng và phạm vi

Quản lý đăng nhập → **Tổng quan hôm nay**, đường dẫn `/Reports/Today`. Trang có năm ô chỉ số, biểu đồ cột và bảng doanh thu theo giờ có thể mở rộng. Khi không có hóa đơn hợp lệ, có thông báo chính xác **Chưa có dữ liệu**, các chỉ số giao dịch bằng 0; số bàn vẫn phản ánh hiện tại. Lỗi SQL có thông báo lỗi và HTTP 503, không giả thành ngày không có giao dịch.

Không có chọn ngày/khoảng ngày, API tải định kỳ hay tự cập nhật. Dùng tải lại trình duyệt để chụp số liệu mới. Chỉ Quản lý được xem, kiểm tra quyền trên controller và menu.

Không cần migration mới cho báo cáo này, không nạp hóa đơn giả vào database đang dùng.

## Giới hạn có sẵn cần lưu ý

Bộ tính báo cáo đã hỗ trợ và kiểm thử lịch qua nửa đêm. Tuy nhiên, cấu hình giờ hoạt động hiện có (`OpeningHoursViewModel`, `usp_SaveOpeningHours` và ràng buộc `CK_OpeningHours`) chỉ cho phép đóng sau mở trong cùng ngày. Vì vậy chưa thể demo lịch qua nửa đêm trực tiếp từ màn hình Giờ hoạt động; cần một thay đổi đồng bộ riêng cho cấu hình và các luồng đặt bàn/QR trước khi bật loại lịch này trong database thật. Task này không tự nới ràng buộc dùng chung, vì các luồng đặt bàn hiện tại cũng dựa vào quy tắc cùng ngày. Đây là phần AC chưa thể nghiệm thu đầu-cuối trên hệ thống hiện tại.

## Kiểm thử

```powershell
dotnet run --project tests/RestaurantManagement.AreaTests -- --daily-overview-only
# Kiểm tra chỉ đọc trên SQL Server đã migrate:
$env:RM_CONNECTION_STRING = 'Server=.\SQLEXPRESS;Database=RestaurantManagement_Dev;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True'
dotnet run --project tests/RestaurantManagement.AreaTests -- --daily-overview-db
```

Kiểm thử bộ tính: đủ năm chỉ số; khung giờ chính xác; mở bao gồm/đóng loại trừ; giờ trống; khung cuối ngắn; tổng cột bằng doanh thu; ngày trống không chia cho 0; qua nửa đêm và chuyển ngày đúng giờ đóng; ngày nghỉ; quyền Quản lý. Kiểm tra SQL chỉ đọc đối chiếu chỉ số với truy vấn tổng hợp độc lập và số bàn vật lý.

Kết quả trên máy phát triển: build thành công; 15 kiểm tra quy tắc/dựng trang Razor và 5 kiểm tra SQL chỉ đọc đạt. Trang Razor được dựng với dữ liệu trống, có doanh thu và lỗi tải. Bộ hồi quy chung dừng ở lỗi có sẵn: `TableDetails.AddItemsQr GET` chưa có trong `ApiAccessMatrix`; báo cáo mới đã có khai báo `DailyOverview.Index GET`. Chưa nghiệm thu hiển thị bằng trình duyệt hoặc toàn bộ luồng qua nửa đêm trên database thật.

Demo: F5, đăng nhập Quản lý, chọn Tổng quan hôm nay. Database hiện chưa có hóa đơn nên dùng để demo trạng thái không có dữ liệu. Để demo cột có doanh thu, thực hiện thanh toán hợp lệ trong giờ hoạt động qua màn hình Thu ngân rồi tải lại trang; không cần tạo tài khoản hoặc đặt lại mật khẩu.
