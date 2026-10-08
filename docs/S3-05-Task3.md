# S3-05 Task 3 — Quản lý huỷ món có tính tiền

Dự án: HeThongDatBan-GoiMon-chinh.

## Quy tắc đã chốt
- Huỷ toàn bộ dòng ở Preparing (Đang chế biến) hoặc Ready (Chờ mang ra), chỉ Quản lý.
- Pending dùng Task 1 (không tính tiền); Served và phiên đã đóng không được huỷ bằng thao tác Task 3.
- Một trong ba lý do bắt buộc: khách đổi ý, gọi nhầm, hết nguyên liệu; quản lý phải xác nhận món vẫn tính toàn bộ tiền.
- Bếp nhận mục riêng “Dừng món — đã huỷ có tính tiền”, không tiếp tục chế biến/mang ra. Dòng rời hàng đợi đang làm; thông báo được giữ trong ca đang mở, kể cả sau khi bàn thanh toán. Tự cập nhật 2 giây, mục tiêu nhận trong 5 giây khi kết nối bình thường.

## Chức năng
Quản lý vào Gọi món → Món đã gửi bếp → Huỷ có tính tiền. Chọn lý do, đánh dấu xác nhận tính tiền và xác nhận. Tạm tính giữ nguyên. Phục vụ vẫn chỉ huỷ Pending không tính tiền.

Nếu trạng thái hoặc số lượng thay đổi khi đang xác nhận, giao diện khoá xác nhận và yêu cầu kiểm tra lại. Máy chủ cũng kiểm tra trạng thái đã đọc lúc mở hộp, quyền, lý do và toàn bộ số lượng trong một giao dịch cùng khoá nghiệp vụ với bếp/thanh toán.

Migration 046_S305PreparedCancellation.sql tạo usp_CancelPreparedOrderItem và nhật ký yêu cầu chống lặp. Ghi OrderItemEvents, người thực hiện, thời điểm, lý do và ChargeWhenCancelled=1. Retry cùng nội dung trả thành công đã xử lý; yêu cầu khác không huỷ lại dòng đã huỷ. Báo cáo cuối ca của Task 2 nhận nhật ký này.

Thanh toán tiếp tục tính giá trị dòng. Trang Hoá đơn → bấm số hoá đơn để mở chi tiết /Cashier/Invoices/{id}, dòng có nhãn “Đã huỷ có tính tiền”, số lượng, đơn giá và thành tiền giữ nguyên từ InvoiceLines. Bàn chỉ còn các món huỷ có tính tiền vẫn được thanh toán.

## Kiểm thử
Build solution. DbTool verify-prepared-cancellation dùng RM_CONNECTION_STRING, tạo database thử riêng và tự xoá. Kiểm tra cả Preparing/Ready với 3 lý do, quyền HTTP/SQL, lý do/xác nhận bắt buộc, CSRF, toàn bộ số lượng, trạng thái thay đổi, giao dịch bếp đồng thời, retry và retry đồng thời, không trùng nhật ký, tiền không giảm, thông báo bếp dưới 5 giây, thanh toán và nhãn hoá đơn, báo cáo cuối ca.
DbTool verify-shift-cancellations duy trì Task 1–2.
node --test tools/tests/pending-order-cancellation.test.cjs tools/tests/shift-cancellation-report.test.cjs kiểm tra giao diện.
AreaTests kiểm tra hồi quy và toàn bộ phân quyền API.

## Demo trên ứng dụng
1. Đăng nhập quản lý, vào Gọi món và gửi món cho bàn đang phục vụ.
2. Tài khoản Bếp vào Màn hình bếp, bấm Bắt đầu nấu (hoặc Đã xong).
3. Quản lý mở Món đã gửi bếp, bấm Huỷ có tính tiền, chọn lý do, xác nhận giữ tiền và huỷ.
4. Quan sát tạm tính không giảm; bếp nhận thông báo dừng món và món biến mất khỏi hàng đợi.
5. Quản lý/Thu ngân thanh toán bàn, vào Hoá đơn và bấm số hoá đơn để thấy nhãn.
6. Quản lý mở Báo cáo cuối ca, chọn ca của đơn để thấy nhật ký có tính tiền.
