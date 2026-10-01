# S1-08 Task 3 — Sắp xếp nhóm món

## Quy tắc

- Chọn nút Lên/Xuống, dùng được với chuột và bàn phím; chưa triển khai kéo thả. Đây là lựa chọn triển khai, chưa có xác nhận trực tiếp từ PO.
- Mở Quản lý nhóm món → Sắp xếp nhóm món (`/DishCategories/Reorder`).
- Thao tác lên/xuống chỉ thay đổi bản xem trước. Bấm Lưu thứ tự mới ghi dữ liệu; Hủy không ghi.
- Khi lưu, phải có đầy đủ mỗi ID nhóm đúng một lần, bao gồm nhóm ngừng sử dụng nếu có. Máy chủ tự đánh lại vị trí 1..N, không trùng hoặc thiếu vị trí.
- Nếu danh sách/thứ tự đã thay đổi sau khi mở trang, từ chối ghi và yêu cầu sắp xếp lại theo danh sách mới.
- Đọc danh sách và cập nhật toàn bộ thứ tự dùng cùng khóa; không trả về trạng thái đang ghi dở. Giữ nguyên ID, tên, trạng thái và liên kết món ăn.
- Thực đơn công khai và gọi món cùng đọc thứ tự đã lưu, chỉ hiển thị nhóm đang sử dụng. Trang đang mở cần tải lại để thấy thứ tự mới.

## Demo

1. Chạy `dotnet run --project src/RestaurantManagement.Web --launch-profile http`.
2. Mở `http://localhost:5105/DishCategories/Reorder`.
3. Bấm Lên ở Đồ uống đến vị trí đầu; bấm Lưu thứ tự.
4. Xem thông báo thành công và tải lại danh sách.
5. Mở `/Menu` và `/Ordering`: Đồ uống đứng đầu ở cả hai trang.
6. Thử chuyển xuống cuối và đổi chỗ hai nhóm rồi lưu lại.

## Kiểm thử ngày 27/09/2026

```powershell
dotnet build RestaurantManagement.sln --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project tests/RestaurantManagement.AreaTests --no-build -- --menu-http
```

Build: 0 cảnh báo, 0 lỗi. Tổng 86 kiểm thử dữ liệu và 39 kiểm tra HTTP đạt. Task 3 bổ sung 10 kiểm thử dữ liệu và 9 kiểm tra HTTP: lên đầu, xuống cuối, đổi chỗ, vị trí liên tiếp, từ chối ID trùng/thiếu/lạ, từ chối danh sách cũ, tải lại trang, thông báo thành công và cùng thứ tự trên hai thực đơn. Chưa chạy tự động tương tác nút JavaScript trong trình duyệt.

## Giới hạn kế thừa

Dữ liệu vẫn lưu trong bộ nhớ: tải lại trang giữ thứ tự, khởi động lại ứng dụng sẽ trở về dữ liệu mẫu. Chưa nối SQL, chưa tích hợp xác thực/phân quyền EP-01. Không bổ sung ngừng sử dụng hoặc xóa nhóm món trong task này.
