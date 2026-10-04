# S1-10 Task 3 — Ngày nghỉ đặc biệt

Từ **Giờ hoạt động**, chọn **Ngày nghỉ đặc biệt** để thêm, sửa ngày/tên/trạng thái áp dụng hoặc xóa sau màn hình xác nhận. Ngày là duy nhất kể cả bản ghi không áp dụng; khi sửa vẫn được giữ nguyên ngày của chính bản ghi đó. Tên bắt buộc, tối đa 150 ký tự.

Chọn **Xem lịch theo ngày**, nhập ngày cụ thể. Ngày nghỉ đang áp dụng được ưu tiên trước lịch tuần và không có lượt. Tắt áp dụng, đổi sang ngày khác hoặc xóa sẽ khôi phục lịch tuần cho ngày cũ. Ngày tuần đóng cửa vẫn không có lượt. Các lượt ngày thường tuân theo Task 2: tăng 30 phút từ giờ mở, giờ nhận bàn phải trước giờ đóng, không trừ thời lượng giữ bàn.

Migration 015 tạo `SpecialHolidays` riêng cho lịch quản lý. Bảng `SpecialDates` cũ thuộc luồng đặt bàn SQL hiện có được giữ nguyên. Bản sửa migration 016 đã nối `SpecialHolidays` vào `usp_CreateReservation`: ngày nghỉ đang áp dụng chặn yêu cầu đặt bàn mới trước khi ghi dữ liệu, ưu tiên hơn lịch tuần và `SpecialDates` cũ. Ngày kiểm tra được lấy từ giờ đặt UTC quy đổi UTC+7 theo quy ước hiện có. Lỗi hiển thị tại trường thời gian. Các đặt bàn đã tồn tại không bị tự động xóa/hủy. Không chuyển đổi múi giờ: màn hình dùng ngày lịch người quản lý chọn (`DateOnly`), chưa hoàn thiện múi giờ toàn hệ thống. Danh tính demo và quyền `Catalog.Manage` dùng chung cấu hình `OpeningHours:ActorUserId` của Task 1.

## Demo và kiểm thử

Chạy migrate theo README, khởi động web. Chọn một ngày có lịch tuần đang mở, thêm ngày nghỉ và xem lịch: không còn khung giờ. Sửa tên/ngày, tắt áp dụng, thử thêm ngày trùng và xóa để kiểm tra các trạng thái.

```powershell
dotnet build RestaurantManagement.sln --no-restore -m:1
dotnet run --project tests/RestaurantManagement.AreaTests --no-build -- --opening-hours-http
```

Bộ HTTP dùng database tạm riêng, kiểm tra thêm/sửa/xóa, trùng ngày khi thêm và sửa, trùng ngày không áp dụng, dữ liệu sai, tải lại, ưu tiên ngày lễ, khôi phục lịch khi tắt/sửa/xóa, chống giả mạo thao tác xóa và bản ghi không tồn tại.
