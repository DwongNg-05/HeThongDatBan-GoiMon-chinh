# S3-05 Task 1 — Huỷ dòng món chờ bếp

Quy tắc đã được người dùng xác nhận: huỷ toàn bộ số lượng trên dòng; huỷ trước chế biến không tính tiền.
Huỷ sau khi bắt đầu chế biến dành cho quản lý và vẫn tính tiền, chưa có giao diện trong Task 1.

## Sử dụng

- Gọi món: chọn bàn có phiên phục vụ đang mở, gửi giỏ món. Dữ liệu được gửi vào OrderBatches/OrderItems qua usp_SubmitOrder, thay cho giỏ đơn demo cũ không gắn bàn. Phiên phải được mở bởi nghiệp vụ đón khách; không tự mở phiên hoặc ca thu ngân trong story này.
- Món đã gọi theo bàn (`/Orders`): xem các dòng món của phiên, trạng thái và tạm tính. Phục vụ/Quản lý có thể huỷ dòng Pending của phiên Open.
- Huỷ món: hộp xác nhận yêu cầu Khách đổi ý, Gọi nhầm hoặc Hết nguyên liệu. Huỷ toàn bộ số lượng; hiển thị dòng đã huỷ và thành tiền 0.
- Hàng đợi bếp (`/Orders/Kitchen`): tự tải lại mỗi 2 giây, loại món đã huỷ. Tài khoản Kitchen có thao tác Bắt đầu chế biến để kiểm tra cạnh tranh với huỷ.
- Khi món đã chế biến, xong hoặc phục vụ, nút huỷ bị vô hiệu và giải thích. Nếu bếp bắt đầu trong lúc chọn lý do, server từ chối, đóng hộp và tải lại trạng thái/tạm tính.

## Quyền và nhất quán

POST có anti-forgery token, người thực hiện lấy từ phiên đăng nhập. SQL kiểm tra tài khoản còn hoạt động và Orders.Manage.
Thủ tục huỷ Pending riêng không cho quản lý vượt kiểm tra Pending trong endpoint của Task 1.
Cùng khoá giao dịch RestaurantOperations với thao tác bếp, cộng UPDLOCK/HOLDLOCK trên dòng, bảo đảm một thao tác thắng khi cạnh tranh.
Nhật ký OrderItemEvents lưu dòng món, Pending→Cancelled, ActorUserId, OccurredAt và lý do. Không cung cấp báo cáo cuối ca ở Task 1.
Gửi lặp cùng người và lý do trên dòng đã huỷ miễn phí trả thành công không đổi dữ liệu; gửi đồng thời chỉ ghi một nhật ký.
Tạm tính sử dụng dòng chưa huỷ hoặc ChargeWhenCancelled=1, không tính các dòng huỷ trước chế biến.

## Cài đặt và kiểm tra

Migration mới: `023_CancelPendingOrderItem.sql`, chạy DbTool migrate với RM_CONNECTION_STRING của môi trường đích.
Đã áp dụng migration vào RestaurantManagement_Dev trên LocalDB, không thay đổi ca/phiên/món đang có.

- `dotnet run --project tests/RestaurantManagement.AreaTests -- --order-cancellation-sql`: SQL thử riêng, tất cả migrations, kiểm tra ba lý do, quyền, missing reason, nhật ký, tổng tiền, retry, cạnh tranh bếp/huỷ; chạy web ở cổng thử để kiểm tra token, đăng nhập và hàng đợi dưới 5 giây. HTTP test dùng bản Web ở `bin/s305` (build bằng `-p:OutputPath=bin/s305/`).
- `node --test tools/tests/order-workflow.test.cjs`: hộp huỷ, bắt buộc lý do, vô hiệu Preparing, gửi lặp, cập nhật 2 giây, tổng tiền và tải lại khi xung đột.

Giới hạn thời gian cập nhật bếp đo trong điều kiện kết nối bình thường. Khi mất kết nối, giao diện hiển thị lỗi và cho Tải lại, không giả vờ huỷ thành công.
