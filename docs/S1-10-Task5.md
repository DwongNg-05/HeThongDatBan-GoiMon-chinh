# S1-10 Task 5 — Asia/Ho_Chi_Minh

Quy tắc đã chốt: form luôn nhập giờ Việt Nam, kể cả khi thiết bị ở nước khác. Chuỗi ISO không offset (`2031-01-07T08:00`) là giờ Việt Nam. Chuỗi có `Z` hoặc offset được coi là một thời điểm và quy đổi sang Việt Nam trước khi kiểm tra. Ví dụ `2031-01-06T20:00-05:00` = `2031-01-07T01:00Z` = `2031-01-07T08:00+07:00`.

`VietnamTime` và `VietnamBookingTimeBinder` dùng chung cho POST đặt bàn và GET kiểm tra lịch. Không để model binder mặc định tự chuyển qua múi giờ máy chủ. Chỉ nhận ISO có ngày và giờ, giây tùy chọn, tối đa 3 chữ số mili giây phù hợp SQL; dữ liệu thiếu, sai hoặc mơ hồ bị từ chối. Khi trả form lỗi, giờ có offset được hiển thị lại thành giờ Việt Nam. Không dùng JavaScript `Date` để chuyển `datetime-local` theo múi giờ thiết bị.

Giờ mở/đóng và khung giờ là giờ địa phương Việt Nam; ngày nghỉ là ngày lịch Việt Nam. Thời điểm đặt bàn lưu UTC như trước, chuyển về Việt Nam khi hiển thị danh sách/chi tiết. Migration 018 dùng `AT TIME ZONE 'UTC' AT TIME ZONE 'SE Asia Standard Time'` trong SQL Server (tên Windows tương ứng Asia/Ho_Chi_Minh), sau đó mới xác định thứ và ngày nghỉ. Không thay đổi thời điểm đã lưu và không đổi múi giờ hệ điều hành.

## Demo

Chọn 08:00 trên form ở thiết bị có múi giờ khác: nhãn ghi rõ giờ Việt Nam, hệ thống vẫn hiểu 08:00 tại Việt Nam. Với lịch mở 00:00–23:59, ngày nghỉ thứ Hai 06/01/2031: `2031-01-05T17:00Z` phải bị chặn vì đã là thứ Hai 00:00 ở Việt Nam; `2031-01-06T17:00Z` là thứ Ba 00:00 và dùng lịch thứ Ba.

## Kiểm thử

Chạy migrate theo README, build và chạy `--opening-hours-http`. Bộ kiểm thử bao gồm múi giờ +07, +09, -05, UTC, nhập không offset, lưu UTC đúng một lần, đọc lại giờ Việt Nam, biên giờ mở/đóng, đổi ngày UTC sang Việt Nam và ưu tiên ngày nghỉ sát 00:00. Các kiểm thử Task 1–4 vẫn chạy cùng bộ.

```powershell
dotnet build RestaurantManagement.sln --no-restore -m:1
dotnet run --project tests/RestaurantManagement.AreaTests --no-build -- --opening-hours-http
```
