# S2-07 Task 3 — chi tiết bàn (AC4)

## Chức năng

Nhân viên Phục vụ mở `/TableMap`, bấm ô bàn để mở bảng chi tiết. Có thể đóng bằng nút ×, bấm ra ngoài hoặc Escape; dialog giữ focus trong bảng và trả focus về ô bàn khi đóng.

API `GET /api/table-map/{code}` chỉ dành cho vai trò `Waiter`; lấy mã tài khoản từ cookie đã xác thực, kiểm tra lại vai trò Phục vụ và trạng thái hoạt động trong SQL trước khi đọc thông tin khách/tạm tính. Thu hồi quyền trong khi đang đăng nhập vẫn bị từ chối. Phản hồi không được cache. Lỗi SQL trả 503 và thông báo thử lại, không trả khách hoặc tiền mẫu thay cho dữ liệu thật. Mã không tồn tại trả 404, mã quá dài trả 400.

## Phương án nghiệp vụ đang triển khai, cần PO duyệt

Chưa có xác nhận PO trong cuộc trao đổi. Các quy tắc dưới đây là phương án thực hiện để review, không ghi nhận là quyết định chính thức:

### Đối chiếu hai yêu cầu chốt với PO

| Quyết định cần chốt | Phương án đã có trong code | Xác nhận PO |
| --- | --- | --- |
| Bàn trống có hiện đặt bàn sắp tới không? | Có, hiện lượt đã xác nhận gần nhất trong tương lai nếu tồn tại. | Chờ xác nhận |
| Bàn đang dọn hiện gì? | Trạng thái đang dọn, không hiện khách/tiền của phiên cũ; hiện đặt bàn tương lai gần nhất nếu có. | Chờ xác nhận |
| Được xem thông tin khách nào? | Tên, số điện thoại đầy đủ, số người; chỉ tài khoản Phục vụ đang hoạt động, có kiểm tra lại quyền ở SQL. | Chờ xác nhận |
| Nhiều đặt bàn trong ngày hiện thế nào? | Hiện lượt tương lai gần nhất và số lượt còn lại; bộ đếm hiện bao gồm cả ngày tương lai khác. Nếu PO chỉ muốn đếm trong ngày, cần thay đổi phạm vi bộ đếm. | Chờ xác nhận |
| Tạm tính gồm món nào? | Dòng món theo đơn giá đã chụp, bỏ món hủy không tính phí, giữ món hủy có tính phí; bàn gộp dùng tổng nhóm thanh toán. | Chờ xác nhận |
| Phụ thu, giảm giá, thuế/phí có tính không? | Chưa đưa các khoản cấp hóa đơn này vào tạm tính; có giải thích trên giao diện. | Chờ xác nhận |
| Có tự cập nhật khi bảng mở không? | Có, mỗi 2 giây và ngay khi nhận thay đổi trạng thái bàn. | Chờ xác nhận |

Không tự đánh dấu các dòng trên là đã chốt chỉ vì code và kiểm thử đã hoàn thành. Khi PO xác nhận, ghi nhận phương án được chọn và chỉnh code nếu quyết định khác phương án hiện tại.

| Trạng thái | Thông tin hiển thị |
| --- | --- |
| Trống | Chưa có khách đang ngồi; chưa bắt đầu phục vụ; không có tạm tính. Nếu có đặt bàn đã xác nhận trong tương lai thì hiện lượt gần nhất. |
| Đã đặt trước | Đặt bàn gần nhất gồm tên, số điện thoại, ngày/giờ Việt Nam và số người; không coi khách đặt là khách đã ngồi. Thiếu đặt bàn thì ghi không có đặt bàn sắp tới. |
| Đang phục vụ | Khách của đặt bàn gắn với phiên đang hoạt động, số người thực tế từ phiên, thời điểm bắt đầu, thời lượng và tạm tính. Phiên vãng lai không có đặt bàn thì ghi “Khách vãng lai” và “Chưa có số điện thoại”. Thiếu phiên thì thông báo chưa có thông tin phiên, không dựng khách/tiền giả. |
| Đang dọn | Không hiện khách, giờ bắt đầu hoặc tiền của phiên đã kết thúc; có thể hiện đặt bàn tương lai gần nhất. |

Chỉ xét đặt bàn `Confirmed` có `StartsAt >= giờ UTC hiện tại`, sắp theo `StartsAt` rồi `Id`. Nhiều lượt thì hiện lượt gần nhất và số lượt đã xác nhận còn lại; lượt cũ hoặc đã hủy không xuất hiện. Số lượt còn lại bao gồm mọi ngày trong tương lai, không giới hạn riêng ngày hiện tại. PO cần xác nhận có muốn giới hạn ngày hoặc liệt kê tất cả lượt hay không.

Tên, số điện thoại đầy đủ và số người chỉ trả cho Phục vụ có quyền hiện tại. PO cần xác nhận có cần che số điện thoại hoặc cho phép vai trò khác hay không.

## Tạm tính

Sử dụng `dbo.vw_SessionTotals`, cùng quy tắc tiền đang có trong hệ thống:

- Tổng `LineTotal` của các món đã gọi, gồm Pending/Preparing/Ready/Served.
- Bỏ món Cancelled không tính phí; món Cancelled có `ChargeWhenCancelled=1` vẫn tính tiền.
- Không cộng phụ thu, thuế/phí hoặc trừ giảm giá ở cấp hóa đơn.
- Chưa có món trong phiên đang phục vụ: 0 ₫. Không có phiên: dấu —, không coi là khoản 0 ₫.
- Bàn đã gộp hiển thị tổng chung của nhóm thanh toán; nội dung này được ghi rõ trong bảng.
- Định dạng VND theo `vi-VN`, không có số lẻ; dùng đơn giá chụp trong dòng món, không tính lại bằng giá thực đơn hiện tại.

PO cần duyệt chính xác các khoản trên, đặc biệt món hủy tính phí và bàn gộp.

## Tự cập nhật

Bảng đang mở gọi lại chi tiết mỗi 2 giây và gọi ngay khi sơ đồ nhận sự kiện trạng thái của bàn đang xem. Việc làm mới cũng cập nhật tiền khi món thay đổi mà trạng thái bàn không đổi. Đóng bảng hoặc ẩn tab sẽ ngừng gọi; hiện lại hoặc nối mạng lại lấy ngay. Yêu cầu cũ bị hủy và kết quả của bàn đã đóng/đổi lựa chọn không ghi đè bàn mới. Poll không gia hạn phiên đăng nhập không hoạt động.

Nếu lỗi tải lần đầu: ẩn nội dung và hiện thông báo/nút Thử lại. Nếu lỗi trong lúc đã xem: giữ dữ liệu lần thành công gần nhất, cảnh báo có thể đã cũ, tự thử lại. Nếu bị từ chối quyền/hết phiên: xóa thông tin khách/tiền đã hiển thị, dừng poll và yêu cầu đăng nhập lại.

## Kiểm thử và demo

- Build solution và chạy bộ kiểm thử .NET hiện có.
- Chạy `tools/tests/table-details.test.cjs` và `tools/tests/table-map-sync.test.cjs` bằng Node: mở/đóng, bấm ra ngoài, focus, tiền VND/0, đặt gần nhất/nhiều lượt, trạng thái đổi khi bảng mở, lỗi tải/thử lại, mất quyền, phản hồi chậm của bàn cũ, ẩn/hiện tab và mất mạng/phục hồi.
- `dotnet run --project tests/RestaurantManagement.AreaTests -- --table-map-sql` (cần RM_CONNECTION_STRING): tạo database kiểm thử riêng và xóa chính database đó sau khi kiểm tra. Bao gồm khách đang ngồi, giờ bắt đầu, tiền 0, tổng món và món hủy, bàn gộp, trống/đặt trước/đang dọn, nhiều đặt bàn, thay đổi trạng thái, tài khoản khác vai trò/vô hiệu hóa/không tồn tại/thu hồi quyền. Không sửa dữ liệu phát triển.

Demo hai máy dùng cùng máy chủ/database: máy thứ nhất mở chi tiết bàn đang phục vụ; đối chiếu tiền với dòng món. Máy thứ hai thay đổi trạng thái bằng quy trình nghiệp vụ trên dữ liệu thử; bảng máy thứ nhất tự cập nhật. Thử bàn đặt trước để thấy đặt bàn gần nhất, và bàn trống/đang dọn để thấy nội dung phù hợp. Database phát triển hiện có thể chưa có phiên phục vụ hoặc đặt bàn để demo; không tạo dữ liệu khách giả trong các bảng nghiệp vụ chỉ để che việc thiếu dữ liệu.

AC4 đã có triển khai và kiểm thử tự động. Chưa nhận xác nhận nghiệp vụ PO, chưa demo trên hai máy thật hoặc kiểm chứng hiệu năng 60 bàn trên máy quầy; không ghi nhận nghiệm thu chính thức khi các bước này còn thiếu.
