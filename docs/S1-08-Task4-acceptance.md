# S1-08 Task 4 — Ngừng sử dụng và bật lại nhóm món

## Hành vi

- Quản lý nhóm món (`/DishCategories`) hiển thị trạng thái và liên kết Ngừng sử dụng/Bật lại theo trạng thái hiện tại.
- Mở liên kết chỉ xem xác nhận, chưa ghi dữ liệu. Bấm xác nhận gửi POST có anti-forgery token; Hủy trở về danh sách.
- Nội dung xác nhận ngừng: “Bạn có chắc muốn ngừng sử dụng nhóm [tên]? Nhóm và các món thuộc nhóm sẽ không hiển thị trên thực đơn công khai và màn hình gọi món. Dữ liệu nhóm và [số món] món ăn liên kết vẫn được giữ nguyên. Bạn có thể bật lại nhóm sau.”
- Nội dung xác nhận và lựa chọn cho phép bật lại là quy tắc triển khai đề xuất, chưa được xác nhận trực tiếp với PO.
- Chỉ thay đổi trạng thái nhóm. Giữ ID, tên, thứ tự, toàn bộ món liên kết và trạng thái bán của từng món. Không bổ sung xóa nhóm.
- Sau khi tải lại, hai thực đơn ẩn nhóm ngừng sử dụng và các món trong nhóm. Giỏ hàng cũ chứa món thuộc nhóm ngừng sử dụng bị từ chối khi gửi.
- Bật lại khôi phục hiển thị nhóm ở thứ tự đã lưu, chỉ hiện các món đang bán. Gửi lặp lại cùng thao tác không đảo ngược trạng thái.

## Demo

1. Chạy `dotnet run --project src/RestaurantManagement.Web --launch-profile http`.
2. Mở `http://localhost:5105/DishCategories`, tạo Món nướng nếu chưa có.
3. Chọn Ngừng sử dụng, đọc nội dung và xác nhận.
4. Nhóm vẫn có trong quản lý, trạng thái Ngừng sử dụng. Tải lại `/Menu` và `/Ordering`: nhóm không xuất hiện.
5. Thử với nhóm có món: món vẫn còn ở Quản lý món; tên, giá và liên kết không đổi.
6. Chọn Bật lại và xác nhận; tải lại hai thực đơn để thấy nhóm trở lại.

## Kiểm thử ngày 27/09/2026

```powershell
dotnet build RestaurantManagement.sln --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project tests/RestaurantManagement.AreaTests --no-build -- --menu-http
```

Build 0 cảnh báo/0 lỗi; 95 kiểm thử dữ liệu và 60 kiểm tra HTTP đạt. Task 4 bổ sung 9 kiểm thử dữ liệu và 21 kiểm tra HTTP, gồm nhóm rỗng/có món, dữ liệu được giữ, hai thực đơn ẩn/hiện nhất quán, bật lại, gửi lặp, giỏ hàng cũ, xác nhận GET không ghi, ID không tồn tại và chống giả mạo yêu cầu.

## Giới hạn hiện tại

Tiếp tục dùng dữ liệu bộ nhớ như Task 1–3: giữ qua tải lại trang, không giữ qua khởi động lại ứng dụng. Chưa nối SQL và chưa tích hợp đăng nhập/phân quyền EP-01. Chưa chạy kiểm thử tương tác trình duyệt tự động; kiểm thử HTTP dùng ứng dụng thật với dữ liệu bộ nhớ riêng.
