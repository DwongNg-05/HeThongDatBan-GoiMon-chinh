# S2-07 Task 2 — tự đồng bộ trạng thái sơ đồ bàn

## Đã triển khai

- `/TableMap` lấy thay đổi mỗi 2 giây khi tab hiển thị, một yêu cầu tại một thời điểm; yêu cầu quá 1,8 giây bị hủy để thử lại.
- `GET /api/table-map/changes?after=<cursor>` chỉ trả các bàn có sự kiện mới, mỗi bàn một trạng thái cuối cùng. Cursor là chuỗi ID sự kiện, không dựa vào đồng hồ trình duyệt hoặc timestamp có thể trùng nhau.
- Snapshot và delta lấy watermark khi giữ khóa chia sẻ nhật ký sự kiện, tránh bỏ qua sự kiện chưa commit có ID nhỏ hơn một sự kiện khác đã commit. Chỉ chuyển cursor sau khi đọc và áp dụng thành công.
- Migration `023_TableMapStatusTimestamp.sql` ghi thời điểm SQL Server cho mọi thay đổi trạng thái, ngay cả khi nguồn cập nhật không truyền timestamp. Gửi cùng trạng thái không sinh sự kiện mới.
- Chỉ đổi màu, nhãn, aria-label và số bàn trống của khu liên quan; không dựng lại danh sách, không điều hướng hoặc cuộn trang. Ô thay đổi nổi bật 3 giây; chế độ giảm chuyển động dùng đường viền thay nhấp nháy.
- Tab ẩn hủy yêu cầu đang chờ và ngừng gọi API; hiện lại gọi ngay. Kết quả của yêu cầu cũ không được áp dụng sau khi chuyển tab/mất mạng.
- Mất mạng, lỗi máy chủ hoặc timeout: giữ dữ liệu đang xem và cảnh báo “Dữ liệu có thể đã cũ”. Có mạng lại thử ngay, đồng bộ thành công tự bỏ cảnh báo.
- Hiển thị lần đồng bộ thành công gần nhất theo giờ Việt Nam. Đây là thời điểm kiểm tra thành công, không phải lúc bàn cuối cùng đổi trạng thái.
- Poll tự động không gia hạn phiên không hoạt động. Mất quyền/hết phiên thì dừng và yêu cầu đăng nhập lại. Lịch sử sự kiện bị đặt lại yêu cầu tải lại snapshot.

## Quyết định PO còn chờ xác nhận

Đề xuất: giữ sơ đồ và cảnh báo dữ liệu có thể đã cũ, hiển thị thời điểm đồng bộ gần nhất. Màn hình này chỉ xem, không có thao tác thay đổi bàn để chặn. Chưa nhận xác nhận PO; không coi đề xuất trên là quyết định đã được duyệt.

Tiêu chí của phương án đang triển khai để PO xác nhận:

1. Mất mạng hoặc không lấy được dữ liệu: giữ trạng thái cuối cùng và hiện “Mất kết nối. Dữ liệu có thể đã cũ. Hệ thống sẽ tự thử lại.”
2. Luôn hiển thị ngày và giờ đồng bộ thành công gần nhất theo giờ Việt Nam; khi có lỗi không cập nhật mốc này. Nếu lần tải đầu tiên thất bại, hiển thị “Chưa đồng bộ được dữ liệu”, không hiển thị thời điểm tải lỗi như một lần thành công.
3. Có mạng trở lại: tự đồng bộ; chỉ gỡ cảnh báo sau khi nhận và áp dụng dữ liệu thành công, không gỡ ngay khi thiết bị báo có mạng.
4. Màn hình chỉ xem: không có thao tác sửa trạng thái để khóa. Phương án chặn thao tác trên các màn hình nghiệp vụ khác cần phạm vi riêng nếu PO yêu cầu.

## Demo

1. Áp dụng migration bằng DbTool `migrate`, dừng phiên Visual Studio cũ và chạy lại ứng dụng.
2. Hai máy truy cập cùng máy chủ ứng dụng và cùng database; không dùng hai database riêng trên mỗi máy. Máy thứ nhất đăng nhập Phục vụ, mở Sơ đồ bàn.
3. Máy thứ hai mở chức năng Quản lý bàn hiện có, sửa trạng thái một bàn rồi lưu. Đây là thao tác demo trên dữ liệu thử; nghiệp vụ thực tế dùng xác nhận đặt bàn, mở phiên phục vụ, thanh toán và hoàn tất dọn theo các thủ tục đã có. API `/api/table-status` cũ chỉ thao tác catalog mẫu trong bộ nhớ, không dùng API đó để thử sơ đồ SQL này.
4. Máy thứ nhất thấy ô đổi màu/nhãn và nổi bật 3 giây; đo từ lúc thao tác được lưu thành công. Khi mạng và máy chủ đáp ứng bình thường, chu kỳ 2 giây + thời gian phản hồi dưới 1,8 giây hướng tới dưới 5 giây. Không cam kết giới hạn này khi mất mạng hoặc máy chủ quá tải.
5. Rút mạng/đặt trình duyệt Offline: hiện cảnh báo; phục hồi mạng: tự đồng bộ các bàn đã đổi. Ẩn tab, thay đổi nhiều bàn rồi mở lại: lấy ngay trạng thái mới nhất.

## Kiểm tra

Build solution; bộ kiểm thử .NET có thêm kiểm tra cursor không hợp lệ và lỗi delta. `tools/tests/table-map-sync.test.cjs` kiểm tra cập nhật một/nhiều ô, số bàn trống, nhãn/màu, nổi bật, giữ tham chiếu ô và vị trí cuộn, mất mạng/phục hồi và ẩn/hiện tab.

`dotnet run --project tests/RestaurantManagement.AreaTests -- --table-map-sql` (cần RM_CONNECTION_STRING) tạo database kiểm thử riêng có tên ngẫu nhiên rồi xóa chính database đó: kiểm tra timestamp trigger, delta rỗng, nhiều bàn, hai kết nối đổi cùng bàn, trạng thái cuối cùng, không phát lại thay đổi đã nhận và độ trễ lấy dữ liệu phía server dưới 5 giây. Không sửa database phát triển.

Chưa đo end-to-end trên hai máy thật, chưa kiểm chứng hiệu năng 60 bàn trên máy quầy. AC3 đã có triển khai và kiểm thử tự động, cần demo đo dưới 5 giây và PO xác nhận xử lý mất mạng để nghiệm thu. Không có bảng chi tiết bàn.
