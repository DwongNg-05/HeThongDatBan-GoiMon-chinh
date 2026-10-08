# S2-08 Task 3 – Tự đặt lại “Tạm hết” lúc 00:00 (Asia/Ho_Chi_Minh)

**Mục tiêu:** tự động đưa toàn bộ món đang tạm hết về trạng thái còn hàng vào 00:00 theo múi giờ Asia/Ho_Chi_Minh.

## Quyết định cần PO xác nhận – nhật ký khi hệ thống tự đặt lại

Đề xuất đã cài đặt: **có ghi nhật ký**. Mỗi món được tự đặt lại ghi **một** dòng vào nhật ký tạm hết (S2-08 Task 2):

- Người thực hiện: để trống (`ChangedBy = NULL`), trên trang **Lịch sử tạm hết** hiển thị “**Hệ thống – tự đặt lại 00:00**”, thao tác “Tự đặt lại lúc 00:00”.
- Trạng thái: Tạm hết → Còn món; thời điểm: lúc tác vụ chạy (ngay sau 00:00).
- Món đang còn hàng: không ghi gì.

Nếu PO quyết định **không** ghi nhật ký cho thao tác tự động: bỏ câu `INSERT dbo.MenuTemporaryOutEvents …` trong `usp_ResetTemporarilyOutMenuItems` (migration mới) và bài kiểm thử “Reset log”.

## Đối chiếu tiêu chí

| Tiêu chí | Cách đáp ứng |
| --- | --- |
| Xác định 00:00 theo Asia/Ho_Chi_Minh | `TemporaryOutResetSchedule` (múi giờ `Asia/Ho_Chi_Minh`, dự phòng `SE Asia Standard Time` trên Windows cũ): 00:00 giờ Việt Nam = 17:00 UTC hôm trước. Thủ tục SQL từ chối mốc không phải 00:00 giờ Việt Nam (51090) hoặc mốc chưa tới (51091). |
| Tác vụ tự động đầu mỗi ngày | `TemporaryOutResetWorker` (chạy nền cùng web): chờ tới 00:00 kế tiếp rồi gọi `dbo.usp_ResetTemporarilyOutMenuItems`. Mỗi mốc chỉ chạy một lần (`dbo.ScheduledJobRuns`). Lỗi kết nối: thử lại sau 1 phút. |
| Chuyển toàn bộ món tạm hết sang còn hàng | Mọi món `IsTemporarilyOut = 1` được báo tạm hết **trước** 00:00 → `0`, trong một giao dịch. |
| Web tắt lúc nửa đêm | Khởi động lại (vd. 9h sáng) → tác vụ chạy bù cho mốc 00:00 hôm nay, nhưng **giữ nguyên** món Bếp vừa báo tạm hết sau 00:00 (lỗi của bản cũ: đặt lại cả những món này). |
| Cập nhật danh sách món trong ngày, thực đơn công khai, màn hình gọi món | Ba màn hình hỏi `GET /api/menu/availability` mỗi 3 giây (S2-08 Task 1) ⇒ thấy món còn hàng trong tối đa 5 giây sau khi đặt lại; món nhận order trở lại. |

## Cài đặt

```powershell
dotnet run --project tools/RestaurantManagement.DbTool -- migrate   # phải thấy "Applied: 040_S208DailyTemporaryOutReset.sql"
```

## Demo

1. Bếp báo **Tạm hết** một món trước 00:00 (hoặc giả lập: đặt món tạm hết rồi xoá lần chạy của mốc hôm nay trong `dbo.ScheduledJobRuns` với `JobName='TemporaryOutReset'`, đổi thời điểm nhật ký bật tạm hết về trước 00:00 rồi khởi động lại web).
2. Sau 00:00 (giờ Việt Nam): món trở lại **còn hàng** trên Món trong ngày, thực đơn công khai và màn hình gọi món; gọi món được.
3. Quản lý mở **Lịch sử tạm hết**: dòng “Tự đặt lại lúc 00:00 – Hệ thống – tự đặt lại 00:00”.

## Kiểm thử

| Bài kiểm thử (theo task) | Ở đâu |
| --- | --- |
| Tạo món đang tạm hết trước thời điểm chuyển ngày | `TemporaryOutResetVerification`: món báo tạm hết lúc 23:59:59 (giờ Việt Nam). |
| Món trở thành còn hàng sau 00:00 | Khởi động web → tác vụ nền chạy cho mốc 00:00 → món còn hàng; nhật ký “Hệ thống”. |
| Món đã còn hàng không bị thay đổi | Món còn hàng: không cập nhật (`UpdatedAt` giữ nguyên), không ghi nhật ký. Chạy lại cùng mốc: không đổi gì. |
| Thời điểm chuyển ngày theo Asia/Ho_Chi_Minh | `TemporaryOutTests` (không cần database): 23:59:59.999 vẫn là ngày cũ, 00:00 giờ Việt Nam = 17:00 UTC, giao thừa. `TemporaryOutResetVerification`: món báo tạm hết lúc 00:00:01 không bị đặt lại; mốc sai giờ bị từ chối. |
| Sau khi đặt lại món nhận order trở lại | API trạng thái, `/Menu`, `/Kitchen/Dishes`, `/Ordering` hiện còn hàng; `/Ordering/Checkout` nhận order; `usp_SubmitOrder` tạo được món. |

```powershell
dotnet build RestaurantManagement.sln --no-restore -m:1
dotnet run --project tests/RestaurantManagement.AreaTests --no-build
dotnet run --no-build --project tools/RestaurantManagement.DbTool -- verify-api-permissions
```
