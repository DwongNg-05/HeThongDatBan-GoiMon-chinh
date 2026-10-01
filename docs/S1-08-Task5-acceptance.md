# S1-08 Task 5 — Xóa nhóm món rỗng

## Quy tắc triển khai

Cho phép xóa vĩnh viễn nhóm không còn món, cả nhóm đang dùng và ngừng sử dụng. Đây là lựa chọn theo demo yêu cầu, chưa có xác nhận trực tiếp từ PO. Có thể chọn ngừng sử dụng thay vì xóa nếu cần giữ nhóm.

- Trang xác nhận chỉ đọc; Hủy không thay đổi dữ liệu.
- Đếm tất cả món liên kết, kể cả món ngừng bán. Nếu còn món, không hiện nút xác nhận xóa; hiển thị số món và liên kết sửa từng món để chuyển nhóm.
- Máy chủ kiểm tra lại số món lúc POST, không tin dữ liệu đếm từ trình duyệt. Yêu cầu xóa nhóm còn món bị từ chối với thông báo: “Không thể xóa nhóm đang chứa [số lượng] món ăn. Vui lòng chuyển toàn bộ món sang nhóm khác trước khi xóa.”
- Thêm/sửa món và xóa nhóm dùng cùng khóa để không tạo món mồ côi khi có yêu cầu đồng thời.
- Xóa nhóm rỗng không xóa món; các nhóm còn lại giữ thứ tự tương đối và được đánh lại vị trí 1..N.
- Thành công chuyển về danh sách với thông báo “Xóa nhóm món thành công.”. Nhóm đã xóa không còn trong danh sách, hai thực đơn hoặc lựa chọn nhóm khi sửa món.
- ID không tồn tại trả 404. POST yêu cầu anti-forgery token.

## Demo

1. Khởi động lại ứng dụng với code mới, mở `/DishCategories`.
2. Chọn Xóa ở Lẩu: thấy thông báo không thể xóa và danh sách món liên kết.
3. Chọn Chuyển sang nhóm khác cạnh từng món, chọn Món chính hoặc nhóm phù hợp rồi lưu.
4. Quay lại xóa Lẩu: khi nhóm rỗng mới thấy xác nhận xóa vĩnh viễn. Có thể Hủy để giữ nhóm.
5. Xác nhận xóa: danh sách không còn Lẩu; món đã chuyển vẫn xuất hiện trong nhóm đích trên hai thực đơn.

## Kiểm thử

Ứng dụng đang chạy giữ tệp build mặc định, nên dùng bản build riêng, không dừng tiến trình của người dùng:

```powershell
dotnet build RestaurantManagement.sln --no-restore -m:1 -p:UseSharedCompilation=false -p:OutputPath=bin/Task5/
dotnet tests/RestaurantManagement.AreaTests/bin/Task5/RestaurantManagement.AreaTests.dll --menu-http
```

Bao phủ nhóm có món/ngừng bán, nhóm rỗng, chuyển món rồi xóa, xác nhận GET và liên kết Hủy, gửi POST trực tiếp, thông báo thành công, dữ liệu sau tải lại, hai thực đơn, ID đã xóa, token và thêm món đồng thời với xóa.

## Giới hạn kế thừa

Dữ liệu vẫn ở bộ nhớ, tồn tại trong tiến trình hiện tại; khởi động lại sẽ nạp lại nhóm/món mẫu, kể cả nhóm mẫu đã xóa. Chưa tích hợp SQL và đăng nhập/phân quyền EP-01. Không coi đây là lưu trữ xóa bền vững. Các kiểm thử HTTP không thay đổi dữ liệu của tiến trình người dùng đang chạy. Chưa kiểm thử trình duyệt tự động.

Kết quả ngày 27/09/2026: bản build riêng thành công, 0 cảnh báo/0 lỗi; 103 kiểm thử dữ liệu và 72 kiểm tra HTTP đạt. Task 5 bổ sung 8 kiểm thử dữ liệu và 12 kiểm tra HTTP.
