# S2-05 — Task 3: Lượt sắp đến trong 30 phút

Hoàn thiện AC 4; AC 1 sắp giờ hẹn tăng dần trong từng nhóm ưu tiên; giữ AC 2 và AC 3.

## Quy tắc đã được người dùng xác nhận

- Dùng thời gian UTC của máy chủ; ngày hiển thị theo Asia/Ho_Chi_Minh (UTC+7).
- Lượt Chờ xác nhận hoặc Đã xác nhận có giờ bắt đầu trong khoảng `[hiện tại, hiện tại + 30 phút]` được đánh dấu. Tính cả đúng hiện tại và đúng 30 phút; loại lượt đã qua, đã huỷ, khách không tới, đã từ chối và khách đã tới.
- Nhóm “Sắp đến trong 30 phút tới” tô nổi bật và đứng đầu, sau đó là “Các lượt đặt còn lại”. Mỗi lượt chỉ xuất hiện một lần. Mỗi nhóm tăng dần theo giờ bắt đầu, cùng giờ theo ID tăng dần.
- Lọc trạng thái trên SQL trước khi nhóm. Bỏ lọc và đầy đủ bảy trường thông tin vẫn hoạt động như Task 1/2; số điện thoại trả về đã được che.
- Tự tải lại sau 30 giây kể từ lần tải trước hoàn tất; quay lại tab cũng tải lại nếu không có yêu cầu đang chạy. Giữ bộ lọc, không làm trống bảng trong lần cập nhật tự động. Thời gian đánh giá được trả về qua `evaluatedAtUtc` và hiển thị giờ cập nhật; máy khách không tự xác định nhóm.
- Khi cửa sổ 30 phút vượt nửa đêm, vẫn chỉ lấy lượt có giờ bắt đầu thuộc ngày đang xem. Chế độ Hôm nay dùng ngày máy chủ trong mỗi lần tải, nên qua nửa đêm tự chuyển sang hôm nay mới và giữ trạng thái lọc. Chọn ngày khác thì giữ ngày đó.

## Demo

1. Đăng nhập và mở Đặt bàn, nhấn Hôm nay.
2. Tạo lượt đặt hợp lệ bắt đầu trong 30 phút tới với trạng thái Chờ xác nhận (hoặc dùng lượt đã xác nhận có sẵn).
3. Lượt xuất hiện trong nhóm nổi bật ở đầu danh sách, chỉ xuất hiện một lần.
4. Chọn Chờ xác nhận/Đã xác nhận để kiểm tra nhóm theo bộ lọc; chọn Đã huỷ/Khách không tới để kiểm tra không đánh dấu.
5. Bỏ lọc. Để trang mở; khi thời gian vượt giờ hẹn, lượt rời nhóm nổi bật ở lần cập nhật tiếp theo, vẫn nằm trong danh sách ngày đó.

## Kiểm tra

Kiểm tra C# các mốc -1s, 0s, +1s, 29m59s, 30m và 30m01s cho cả sáu trạng thái.
SQL thử riêng kiểm tra nhiều lượt, thứ tự từng nhóm, không lặp, kết hợp lọc, thay đổi thời gian, nhóm rỗng và qua nửa đêm.
JavaScript kiểm tra hiển thị nhóm, đủ trường, cập nhật 30 giây giữ lọc, không làm trống bảng và ngày hôm nay mới từ máy chủ.

Chạy như các task trước bằng `--daily-reservations-sql` và `node --test tools/tests/daily-reservations.test.cjs`.
Không tạo hoặc sửa lượt đặt trong dữ liệu nhà hàng khi chạy kiểm tra.
