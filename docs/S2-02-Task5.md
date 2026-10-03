# S2-02 Task 5 - Khung giờ còn bàn theo sức chứa

Quy ước triển khai:

- Mỗi đơn `Pending`, `Confirmed` hoặc `Arrived` giữ một bàn trong khoảng từ giờ nhận bàn đến hết thời lượng đặt bàn.
- Khách chọn khu vực là yêu cầu ưu tiên: khi khu vực đó không còn bàn đủ chỗ, form báo hết chỗ và khách có thể đổi khu vực.
- Không ghép bàn; một bàn có `MaxCapacity` phải lớn hơn hoặc bằng số khách.

API khung giờ nhận thêm `guestCount` và `preferredAreaId`, nên tải lại khi khách đổi ngày, số khách hoặc khu vực. Stored procedure chọn bàn phù hợp có sức chứa nhỏ nhất và chạy dưới khóa giao dịch; vì vậy hai yêu cầu gần như đồng thời không thể cùng giữ bàn cuối cùng. Nếu bàn vừa hết trong lúc khách đang nhập, máy chủ trả về thông báo rõ ràng mà không làm mất dữ liệu biểu mẫu.
