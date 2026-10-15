# S4-06 Task 1 — Tổng quan doanh thu trong ngày

Trang Quản lý tại `/Reports/Daily` hiển thị doanh thu, số hoá đơn, lượt khách, giá trị trung bình mỗi hoá đơn và số bàn đang phục vụ cho ngày nghiệp vụ hiện tại theo giờ Việt Nam. Ngày không có hoá đơn được hiển thị rõ ràng; bốn chỉ số giao dịch bằng 0, còn số bàn đang phục vụ vẫn phản ánh thời điểm truy vấn.

## Quy tắc dữ liệu

- Chỉ hoá đơn `Paid` được tính. `Total` đã là số tiền sau giảm giá.
- Hoá đơn `Voided` bị loại. Schema hiện có chỉ phân biệt `Paid` và `Voided`, chưa có trạng thái hoàn tiền riêng; do đó hoàn toàn phần phải được thể hiện bằng `Voided`. Không thể tự khấu trừ hoàn tiền một phần từ dữ liệu hiện có.
- Lượt khách là tổng `GuestCount` đã chốt trên các hoá đơn được tính.
- Trung bình là doanh thu chia số hoá đơn `Paid`; khi không có hoá đơn thì bằng 0.
- Bàn đang phục vụ là bàn active trong khu vực active có trạng thái `Serving`. Bàn đã thanh toán ở trạng thái `Cleaning` không được tính.

Các quy tắc trên bám theo schema và luồng checkout đang có; dự án không có ghi nhận quyết định PO riêng về cách xử lý hoàn tiền một phần.

## Biểu đồ theo giờ (Task 2)

Trang tổng quan dùng cùng ngày nghiệp vụ hôm nay cho cả năm chỉ số và biểu đồ. Hoá đơn được gán theo `IssuedAt` (thời điểm phát hành/thanh toán), đổi từ UTC sang giờ Việt Nam; mốc đúng đầu giờ thuộc khung giờ mới, khung giờ kết thúc không bao gồm mốc đóng cửa. Các giờ trong khoảng mở cửa đều xuất hiện, kể cả giờ doanh thu bằng 0. `SpecialDates` có ưu tiên cao hơn lịch tuần; ngày lễ đặc biệt đang bật được coi là đóng cửa. Lịch qua nửa đêm được vẽ tiếp với nhãn giờ sau 00:00.

Lịch cấu hình hiện có ràng buộc `ClosesAt > OpensAt`, nên giao diện quản lý chưa thể lưu giờ qua nửa đêm. Bộ tổng hợp đã xử lý lịch qua đêm nếu dữ liệu được nới ràng buộc ở phiên bản sau. Dữ liệu hiện hành cũng dùng ngày lịch Việt Nam làm `BusinessDate`; biểu đồ chỉ lấy hoá đơn mang ngày đó.

## Bộ lọc và cập nhật tự động (Tasks 3–4)

Khoảng ngày mặc định là hôm nay; hai đầu mút tính cả ngày và khoảng tối đa 92 ngày. Biểu đồ một ngày tiếp tục theo giờ mở cửa của ngày đó; biểu đồ nhiều ngày tổng hợp theo giờ trên đồng hồ Việt Nam (00:00–23:00) cho các hoá đơn trong khoảng. Số bàn đang phục vụ là snapshot tại lúc truy vấn, không cộng các lượt đếm của từng ngày.

Trang tự lấy lại toàn bộ thống kê mỗi 30 giây, dùng nguyên khoảng ngày đang hiển thị. Mỗi lần đọc lại snapshot hóa đơn `Paid` trực tiếp từ database nên không cộng dồn bản refresh trước; khi tải lỗi, số cũ được giữ lại và có nút thử lại. Thời điểm cập nhật gần nhất chỉ đổi sau khi tải dữ liệu thành công.
