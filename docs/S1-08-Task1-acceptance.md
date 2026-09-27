# S1-08 Task 1 — Thực đơn theo nhóm món

## Quy tắc nghiệm thu

- Danh sách theo yêu cầu: Khai vị → Món chính → Lẩu → Tráng miệng → Đồ uống (thứ tự 1–5). Đây là danh sách được triển khai theo đặc tả; việc xác nhận với PO do nhóm dự án thực hiện.
- Nhóm có tên, trạng thái sử dụng và thứ tự hiển thị. Chỉ nhóm đang sử dụng xuất hiện; sắp xếp theo thứ tự hiển thị, rồi theo ID nếu trùng thứ tự.
- Mỗi món liên kết đến một nhóm tồn tại; thao tác thêm/sửa từ chối nhóm không hợp lệ.
- Chỉ món đang bán xuất hiện. Nhóm không có món hoặc không còn món đang bán vẫn hiện tiêu đề và “Chưa có món ăn”.
- `/ThucDon` và `/GoiMon` dùng chung truy vấn và giao diện nhóm món, có liên kết chuyển nhanh đến từng nhóm.
- Không bổ sung màn hình quản lý nhóm món trong task này.

## Phạm vi dữ liệu

Hai màn hình tiếp tục sử dụng `InMemoryQuanLyMonStore` hiện có. Khi khởi động, ứng dụng tạo 5 nhóm và 5 món mẫu, mỗi nhóm một món. Các thay đổi trong quản lý món chỉ tồn tại trong tiến trình và mất khi khởi động lại. Chưa kết nối hai màn hình này với bảng SQL `MenuCategories`/`MenuItems`; không thay đổi migration hoặc database đang chạy. SQL hiện đã có tên, trạng thái, thứ tự và khóa ngoại món–nhóm.

## Demo

```powershell
dotnet run --project src/RestaurantManagement.Web --launch-profile http
```

1. Mở `http://localhost:5105/ThucDon` và `http://localhost:5105/GoiMon`.
2. Kiểm tra đúng 5 nhóm theo thứ tự trên; lần lượt thấy Gỏi cuốn, Cơm chiên hải sản, Lẩu Thái, Chè hạt sen, Trà đào.
3. Bấm tên nhóm ở đầu trang để chuyển nhanh đến nhóm đó.
4. Vào Quản lý món, sửa Gỏi cuốn thành Ngưng bán. Tải lại cả hai trang: Khai vị vẫn đứng đầu, hiển thị “Chưa có món ăn”.
5. Bật bán lại hoặc tạo món mới thuộc Khai vị: món xuất hiện dưới đúng nhóm trên cả hai trang.

## Kiểm thử tự động

```powershell
dotnet restore RestaurantManagement.sln --locked-mode
dotnet build RestaurantManagement.sln --no-restore -m:1
dotnet run --project tests/RestaurantManagement.AreaTests --no-build -- --menu-http
```

Kiểm thử dữ liệu kiểm tra 5 nhóm mặc định, liên kết món, thứ tự khác ID, nhóm rỗng, món ngừng bán, nhóm ngừng sử dụng, hai PageModel nhất quán, từ chối nhóm không tồn tại và không có nhóm đang sử dụng.

Kiểm thử HTTP tự mở một tiến trình web riêng, kiểm tra HTML của cả hai trang, món đúng nhóm, form giỏ hàng và thao tác ngừng bán món cuối cùng qua màn hình sửa món. Tiến trình dùng dữ liệu bộ nhớ riêng, không kết nối database thật, tự đóng sau kiểm thử.

Đã xử lý dấu xung đột còn sót trong Program và layout, giữ đăng ký MVC/SQL hiện có và Razor Pages; bổ sung layout/tag helper cho Razor Pages để liên kết và form hoạt động.

Kết quả ngày 27/09/2026: build thành công; 59 kiểm thử dữ liệu (12 kiểm thử nhóm món mới) và 12 kiểm tra HTTP đạt. Kiểm tra HTTP xác nhận nhóm Khai vị rỗng vẫn giữ đúng vị trí trên cả hai màn hình. Chưa kiểm tra tương tác JavaScript bằng trình duyệt tự động. Bổ sung trang Razor Checkout bị thiếu để form giỏ hàng tạo được endpoint hợp lệ.
