# S3-05 Task 1 — Huỷ món chờ bếp

Phạm vi PO: huỷ toàn bộ số lượng trên dòng món. Ba lý do bắt buộc: khách đổi ý, gọi nhầm, hết nguyên liệu. Phần huỷ khi còn Pending không tính tiền. Quy tắc có sẵn cho quản lý huỷ sau chế biến (`ChargeWhenCancelled=1`) được giữ nguyên; task này không thêm thao tác quản lý hay báo cáo cuối ca.

## Demo

1. Đăng nhập Phục vụ, mở Gọi món → Món đã gửi bếp (`/Ordering/Sent`).
2. Chọn dòng Chờ bếp, kiểm tra số lượng toàn bộ dòng và chọn một lý do, xác nhận.
3. Dòng chuyển Đã huỷ và biến mất khỏi hàng đợi bếp; không cho huỷ một phần kể cả gửi yêu cầu trực tiếp.
4. Tạm tính giảm bằng số lượng huỷ × đơn giá. Mở màn hình bếp trên tài khoản Bếp: hàng đợi tự đọc lại mỗi 2 giây (mục tiêu dưới 5 giây khi kết nối bình thường).
5. Khi bếp đã bắt đầu chế biến, nút huỷ bị khoá và có giải thích. Nếu bếp bắt đầu trong lúc hộp xác nhận mở, máy chủ từ chối và giao diện đọc lại trạng thái.

## Tính đúng và nhật ký

Migration `043_S305PendingCancellation.sql` và `044_S305WholeLineCancellation.sql` bổ sung thủ tục riêng cho món Pending. Huỷ, chuyển trạng thái bếp và thanh toán cùng dùng `usp_LockOperations`, nên kiểm tra trạng thái và cập nhật là một giao dịch. Phần huỷ được giữ trong `OrderItems`; giá trị tạm tính/hoá đơn có sẵn loại `Cancelled` không tính tiền. `PendingOrderCancellations` lưu dòng gốc, dòng huỷ, số lượng, người, thời điểm, lý do và mã yêu cầu. `OrderItemEvents` ghi thêm sự kiện huỷ. Nhật ký chỉ được thêm.

Quyền kiểm tra tại endpoint và bằng `Orders.Manage` trong SQL (tài khoản đang hoạt động). POST yêu cầu chống CSRF. Mã yêu cầu dùng lại cùng nội dung trả thành công đã xử lý; dùng lại khác nội dung bị từ chối. Không có endpoint huỷ món đang chế biến trong task này.

## Kiểm thử

Build solution và chạy `dotnet run --project tools/RestaurantManagement.DbTool --no-build -- verify-pending-cancellation` với `RM_CONNECTION_STRING` trỏ tới SQL Server phát triển. Lệnh tạo database kiểm thử riêng, kiểm tra HTTP + SQL và xoá đúng database tạm đó sau khi hoàn tất. Không nạp dữ liệu demo hay thay đổi đơn đang phục vụ trong database ứng dụng.
