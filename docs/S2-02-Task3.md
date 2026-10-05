# S2-02 Task 3 - Giới hạn 3 đơn chờ xác nhận

Mỗi số điện thoại đã chuẩn hóa chỉ có tối đa 3 đơn ở trạng thái `Pending`.

- Đơn `Confirmed`, `Rejected`, `Cancelled`, `Arrived` và `NoShow` không được tính vào giới hạn.
- Việc đếm và tạo đơn được thực hiện trong cùng giao dịch, dưới khóa `usp_LockOperations`; vì vậy hai yêu cầu gần như đồng thời không thể cùng vượt quá giới hạn.
- Khi chạm giới hạn, biểu mẫu giữ nguyên dữ liệu và báo: khách đã có 3 đơn chờ xác nhận, hãy chờ quán xác nhận hoặc gọi nhà hàng để được hỗ trợ.
- Theo quy ước Task 2, hệ thống chỉ nhận số điện thoại dạng 10 chữ số bắt đầu bằng `0`; các cách viết khác bị từ chối trước khi đếm.
