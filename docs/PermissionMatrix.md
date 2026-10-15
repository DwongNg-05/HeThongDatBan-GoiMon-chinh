# API nhạy cảm và ma trận phân quyền

Quy tắc hiện hành của hệ thống được giữ nguyên: chỉ **Quản lý** sửa giá món; **Quản lý** và **Thu ngân** được xem doanh thu. Ma trận hiển thị các module trên thanh điều hướng và đọc quyền từ cùng cấu hình `RoleNavigation.Items`.

| Chức năng | API | Vai trò được phép |
| --- | --- | --- |
| Sửa giá món | `PUT /api/menu/{id}/price` | Quản lý |
| Xem doanh thu theo ngày kinh doanh | `GET /api/reports/revenue?date=YYYY-MM-DD` | Quản lý, Thu ngân |
| Xem cấu hình ma trận | `GET /api/permissions/matrix` | Quản lý |
| Màn hình ma trận | `GET /PermissionMatrix` | Quản lý |

API cập nhật giá nhận JSON `{ "priceVnd": 125000 }`, giới hạn từ 1 đến 50.000.000 VND. API dùng phiên đăng nhập cookie và mã chống giả mạo từ biểu mẫu sửa món; thay đổi giá đi qua cùng `IMenuStore` và thủ tục SQL, giữ nhật ký giá hiện có.

API nhạy cảm trả lỗi JSON **401** khi chưa đăng nhập và **403** khi vai trò không được phép. API doanh thu chỉ tổng hợp hoá đơn đã thanh toán, tách doanh thu tiền mặt/chuyển khoản và có thể nhận ngày `date`.

Đăng nhập tài khoản Quản lý rồi mở `/PermissionMatrix` để xem bảng vai trò – module. API JSON có thể kiểm tra trực tiếp tại `/api/permissions/matrix`. Kiểm tra tự động bằng `dotnet run --project tests/RestaurantManagement.AreaTests`; kiểm thử HTTP trên SQL Server bằng `dotnet run --project tools/RestaurantManagement.DbTool -- verify-api-permissions`.
