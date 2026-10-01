# Sơ đồ bàn — Task 1 (AC1, AC2)

Mở `/TableMap` bằng tài khoản vai trò `Waiter`. `/api/table-map` trả snapshot bàn nhóm theo khu vực. Trang Home chuyển tới màn hình này. Vai trò khác bị từ chối cả trang lẫn API sơ đồ; cookie web chuyển tới trang từ chối truy cập. Các API sơ đồ cũ cũng được bảo vệ cùng vai trò.

## Cấu hình demo cần PO xác nhận

Chưa có xác nhận PO trong yêu cầu. Những giá trị dưới đây chỉ là đề xuất demo:

| Thứ tự | Khu vực | Mã bàn | Số bàn |
| --- | --- | --- | --- |
| 1 | Tầng một | A01–A20 | 20 |
| 2 | Tầng hai | B01–B20 | 20 |
| 3 | Sân vườn | C01–C20 | 20 |

Trong mỗi khu: bàn 10 và 20 chứa 8 người; bàn 03, 06, 09, 12, 15, 18 chứa 6 người; còn lại chứa 4 người. Dữ liệu mẫu hiện có đủ bốn trạng thái, dùng chung DemoTableCatalog.

| Trạng thái | Nhãn chữ | Màu nền | Màu chỉ báo |
| --- | --- | --- | --- |
| Available | Trống | #e8f5ec | #25834f |
| Reserved | Đã đặt trước | #eeedfc | #7161bd |
| Serving | Đang phục vụ | #fff1d8 | #b57413 |
| Cleaning | Đang dọn | #e2f3f6 | #318596 |

Quyền đề xuất theo yêu cầu: chỉ Phục vụ (`Waiter`). PO cần xác nhận danh sách khu vực thực tế, sức chứa, thứ tự và bảng màu trên; đồng thời chốt thời điểm chuyển bàn thành “Đã đặt trước”. Task này hiển thị trạng thái hiện tại trong catalog, không tự suy ra từ thời gian đặt và không tự đặt một ngưỡng phút chưa được xác nhận.

## Demo

Tài khoản mẫu `waiter` / `0900000002` được tạo bởi `seed-demo` trên database mới, mật khẩu do người chạy seed cung cấp theo README. Nếu database đã có dữ liệu, quản lý tạo tài khoản Phục vụ trong chức năng quản lý tài khoản; không nạp seed vào database hiện có.

Đăng nhập → Sơ đồ bàn → thấy 3 khu và 60 bàn, chú giải ở đầu trang, mã bàn/sức chứa/nhãn trên từng ô và số bàn trống từng khu. Dùng nút Tải lại sơ đồ để lấy snapshot mới.

## Kiểm tra và giới hạn

`dotnet run --project tests/RestaurantManagement.AreaTests` kiểm tra nhóm bàn, thứ tự, khu vực rỗng, không có khu vực, lỗi trang/API và chính sách quyền Waiter/Manager/Kitchen/Cashier/ẩn danh. Bộ kiểm thử hiện có kiểm tra bốn nhãn và lớp màu.

Kiểm tra thủ công thang xám: bật mô phỏng grayscale trong trình duyệt, xác nhận trên từng ô vẫn đọc được Trống/Đã đặt trước/Đang phục vụ/Đang dọn, sức chứa và mã bàn. Chưa thực hiện kiểm chứng trực quan thang xám hoặc đo tốc độ trên máy quầy.

Dữ liệu màn hình hiện đọc từ SQL bằng `SqlTableMapReader`: khu vực đang hoạt động, bàn đang hoạt động, sức chứa `MaxCapacity`, thứ tự khu vực `SortOrder` rồi `Id`, thứ tự bàn `SortOrder` rồi mã bàn. Khu vực không có bàn vẫn hiển thị. Lỗi SQL trả thông báo lỗi, không thay dữ liệu thật bằng mẫu. `DemoTableCatalog` 60 bàn được giữ phục vụ kiểm thử, không còn là nguồn dữ liệu của màn hình.

Kiểm tra database cục bộ ngày 02/10/2026: Tầng một 10 bàn, Tầng hai 10 bàn, Sân vườn 5 bàn; sức chứa từ 4 đến 12, tổng 25 bàn và đều Available. Đây là dữ liệu hiện có, chưa được PO xác nhận. Chưa bổ sung hoặc thay đổi dữ liệu bàn hiện có chỉ để khớp mẫu 60 bàn.

Màn hình Task 1 không dùng cập nhật trực tiếp, bảng chi tiết hoặc sửa trạng thái. Các dịch vụ cũ vẫn được giữ để tránh xoá chức năng đã có. AC1/AC2 cần PO duyệt cấu hình và chạy nghiệm thu demo trước khi xác nhận hoàn tất nghiệp vụ. Quy tắc thời gian đặt trước đang chờ xác nhận; hiện hiển thị trạng thái lưu trong SQL.
