# S1-08 Task 2 — Thêm nhóm món và sửa tên

## Quy tắc đang áp dụng

Các quy tắc dưới đây là lựa chọn triển khai theo yêu cầu, chưa có xác nhận trực tiếp từ PO:

- Chuẩn hóa Unicode NFC, bỏ khoảng trắng đầu/cuối, gộp các khoảng trắng liên tiếp thành một dấu cách.
- So sánh tên không phân biệt hoa/thường, có phân biệt dấu tiếng Việt. Ví dụ ` MÓN   NƯỚNG ` trùng `Món nướng`; `Mon nuong` khác `Món nướng`.
- Tên sau chuẩn hóa phải có 1–50 ký tự (độ dài chuỗi .NET); giữ cách viết hoa/thường người dùng nhập.
- Kiểm tra trùng trên cả nhóm đang sử dụng và ngừng sử dụng. Khi sửa, loại chính ID đang sửa khỏi kiểm tra trùng.
- Kiểm tra và ghi tên nằm trong cùng khóa để hai yêu cầu đồng thời không tạo nhóm trùng nhau.

Thông báo:

- Tên nhóm món không được để trống.
- Tên nhóm món không được vượt quá 50 ký tự.
- Tên nhóm món đã tồn tại. Vui lòng nhập tên khác.
- Thêm nhóm món thành công.
- Sửa tên nhóm món thành công.

## Giao diện và demo

Chạy ứng dụng với profile `http`, mở `http://localhost:5105/DishCategories` hoặc chọn **Quản lý nhóm món** trên thanh điều hướng.

1. Chọn **Thêm nhóm món**, nhập `Món nướng`. Trạng thái mặc định đang sử dụng; thứ tự mặc định đặt sau nhóm cuối. Có thể chọn trạng thái và thứ tự khi tạo.
2. Lưu: quay về danh sách và thấy thông báo thành công. Tải lại trang vẫn thấy nhóm mới.
3. Chọn **Sửa tên**, nhập `Món nướng BBQ`, lưu và tải lại để xác nhận.
4. Mở `/Menu` và `/Ordering`: cùng hiển thị tên mới và thông báo nhóm chưa có món.
5. Thử tên rỗng, 51 ký tự hoặc ` KHAI   VỊ `: ở lại form, giữ dữ liệu nhập và báo lỗi; không ghi dữ liệu sai.

Màn hình sửa chỉ nhận tên; trạng thái và thứ tự hiện có không bị thay đổi. Không có thao tác đổi thứ tự/ngừng sử dụng nhóm hiện có trong task này. Đổi tên giữ nguyên ID nên các món vẫn liên kết đến nhóm cũ.

## Kiểm thử

```powershell
dotnet build RestaurantManagement.sln --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project tests/RestaurantManagement.AreaTests --no-build -- --menu-http
```

Kết quả 27/09/2026: build thành công, 0 cảnh báo, 0 lỗi; 76 kiểm thử dữ liệu và 30 kiểm tra HTTP đạt. Task 2 bổ sung 17 kiểm thử dữ liệu và 18 kiểm tra HTTP. Có kiểm tra chuẩn hóa tên, giới hạn 50/51 ký tự, thêm/sửa trùng tên, cùng tên của chính nhóm, tên nhóm ngừng sử dụng, thêm đồng thời, giữ thứ tự/trạng thái khi sửa, thông báo thành công, tải lại trang, 404 và chống giả mạo yêu cầu. Kiểm thử Task 1 vẫn đạt.

## Giới hạn tích hợp hiện tại

- Tiếp tục dùng singleton `InMemoryMenuStore`: dữ liệu tồn tại qua tải lại trang, không tồn tại qua khởi động lại ứng dụng, không chia sẻ giữa nhiều tiến trình. Chưa nối SQL.
- Nhánh EP-02 hiện chưa có đăng nhập/phân quyền web. Màn hình mang chức năng dành cho quản lý nhưng chưa giới hạn truy cập theo vai trò; cần tích hợp phần xác thực EP-01 để áp dụng quyền quản lý.
- Chưa kiểm thử bằng trình duyệt tự động; đã kiểm thử HTTP và HTML thực tế.
