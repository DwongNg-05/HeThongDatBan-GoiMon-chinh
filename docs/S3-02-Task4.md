# S3-02 Task 4 — Gửi lại order an toàn khi mất mạng

- Mỗi giỏ gọi món có một `RequestId` (UUID) được giữ trong phiên trình duyệt cho đến khi nhận được xác nhận thành công.
- Khi bấm gửi, nút bị khóa. Trang chờ tối đa 15 giây; nếu mất mạng, hết thời gian chờ hoặc phản hồi lỗi, giỏ và `RequestId` vẫn còn nguyên để khách bấm gửi lại.
- `OrderBatches.RequestId` đã có ràng buộc duy nhất ở cơ sở dữ liệu. Stored procedure `usp_SubmitOrder` khóa giao dịch và trả lại order cũ nếu cùng `RequestId` đến lần nữa. Vì vậy hai lần gửi đồng thời hoặc gửi lại sau khi phản hồi bị mất chỉ tạo một order.
- Sau khi thành công, trình duyệt xóa `RequestId`, nên lần gọi món kế tiếp là một order mới, kể cả nội dung giống hoàn toàn.
- Nếu máy chủ đã lưu order nhưng phản hồi bị mất, lần gửi lại tìm order cũ trước khi kiểm tra giỏ hay giá hiện tại và hiển thị đúng hóa đơn gốc.
