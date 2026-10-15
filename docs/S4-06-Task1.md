# S4-06 Task 1 — Tổng quan doanh thu trong ngày

Trang Quản lý tại `/Reports/Daily` hiển thị doanh thu, số hoá đơn, lượt khách, giá trị trung bình mỗi hoá đơn và số bàn đang phục vụ cho ngày nghiệp vụ hiện tại theo giờ Việt Nam. Ngày không có hoá đơn được hiển thị rõ ràng; bốn chỉ số giao dịch bằng 0, còn số bàn đang phục vụ vẫn phản ánh thời điểm truy vấn.

## Quy tắc dữ liệu

- Chỉ hoá đơn `Paid` được tính. `Total` đã là số tiền sau giảm giá.
- Hoá đơn `Voided` bị loại. Schema hiện có chỉ phân biệt `Paid` và `Voided`, chưa có trạng thái hoàn tiền riêng; do đó hoàn toàn phần phải được thể hiện bằng `Voided`. Không thể tự khấu trừ hoàn tiền một phần từ dữ liệu hiện có.
- Lượt khách là tổng `GuestCount` đã chốt trên các hoá đơn được tính.
- Trung bình là doanh thu chia số hoá đơn `Paid`; khi không có hoá đơn thì bằng 0.
- Bàn đang phục vụ là bàn active trong khu vực active có trạng thái `Serving`. Bàn đã thanh toán ở trạng thái `Cleaning` không được tính.

Các quy tắc trên bám theo schema và luồng checkout đang có; dự án không có ghi nhận quyết định PO riêng về cách xử lý hoàn tiền một phần.
