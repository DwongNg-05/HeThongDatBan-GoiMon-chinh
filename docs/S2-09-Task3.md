# S2-09 Task 3 — Trạng thái email xác nhận trong chi tiết đặt bàn

Nhân viên mở chi tiết một lượt đặt bàn (`/Reservations/Details/{id}`) và thấy khu vực **Email xác nhận** gồm:
- trạng thái gửi;
- số lần thử và số lần gửi lại;
- thời điểm thử gửi gần nhất;
- kết quả cuối cùng;
- thông tin lỗi khi email không gửi được.

Trạng thái email nằm riêng với **Trạng thái đặt bàn**.

**AC hoàn tất:** nhân viên xem được trạng thái gửi email của từng lượt đặt bàn trong màn hình chi tiết.

## Dữ liệu

Hàng đợi `dbo.EmailOutbox` (migration 001/002/003) đã lưu theo từng lượt đặt bàn (`ReservationId`) các thông tin: `Status`, `AttemptCount` (tối đa 4), `NextAttemptAt`, `SentAt`, `LastError`.

Migration **`024_ReservationEmailStatus.sql`** bổ sung:
- Cột `LastAttemptAt`, ghi thời điểm thử gửi gần nhất. Dữ liệu cũ được ước lượng từ `SentAt` hoặc `NextAttemptAt − 5 phút`.
- Thủ tục `usp_ClaimEmail` ghi `LastAttemptAt = SYSUTCDATETIME()` mỗi khi worker nhận một lần thử.
- Thủ tục `usp_CompleteEmail`: khi gửi thành công thì xoá lỗi của lần thử trước.
- Chỉ mục `IX_EmailOutbox_Reservation` để tra cứu theo lượt đặt bàn.

> Nếu nhánh khác cũng đã thêm migration số `024`, đổi tên file này thành số tiếp theo trước khi merge. Không sửa migration đã chạy.

## Quy tắc hiển thị

| Dữ liệu outbox | Trạng thái email | Kết quả cuối cùng | Lỗi |
| --- | --- | --- | --- |
| `Pending`, `AttemptCount = 0` | **Đang chờ gửi** | Chưa có kết quả cuối cùng | Không |
| `Processing` | **Đang gửi** | Chưa có kết quả cuối cùng | Lỗi lần thử trước, nếu đây không phải lần thử đầu |
| `Pending`, `AttemptCount > 0` | **Đang thử gửi lại** (kèm giờ thử lại) | Chưa có kết quả cuối cùng | Lỗi lần thử trước |
| `Sent` | **Đã gửi thành công** | Thành công lúc … | Không, kể cả khi còn lỗi cũ |
| `Failed` | **Gửi thất bại** | Thất bại sau 4 lần thử | Có |
| `Cancelled` | Đã huỷ gửi | Đã huỷ, không gửi nữa | Không |

- **Số lần thử** hiển thị dạng `2/4 (1 lần gửi lại)`. Thời gian hiển thị theo giờ Việt Nam.
- **Loại email:**
  - `BookingReceived`: Xác nhận đã nhận yêu cầu đặt bàn
  - `BookingConfirmed`: Xác nhận đặt bàn thành công
  - `BookingRejected`: Thông báo từ chối
  - `BookingCancelled`: Thông báo huỷ
  - `BookingReminder`: Nhắc lịch
  
  Email mới nhất hiển thị trước.
- **Khách không để lại email:** trang ghi rõ hệ thống không gửi email xác nhận.
- **Tách trạng thái đặt bàn với trạng thái email:** dòng *Trạng thái đặt bàn* dùng nhãn riêng (Chờ xác nhận, Đã xác nhận, …). Khu vực email có ghi chú: *“lượt đặt bàn vẫn có hiệu lực kể cả khi email gửi thất bại”*. Không có nhãn email nào trùng với nhãn trạng thái đặt bàn.
- **Không lẫn dữ liệu giữa các lượt đặt bàn:** câu SQL lọc `WHERE ReservationId=@reservationId`, và `ReservationEmailStatus.BuildPanel` lọc thêm một lần theo mã lượt đặt bàn. Vì vậy lỗi của lượt này không bao giờ hiển thị ở lượt khác.
- **Tự cập nhật khi có kết quả gửi mới:**
  - Khi còn email chưa có kết quả cuối cùng, trang gọi `GET /Reservations/EmailStatus/{id}` mỗi 15 giây. Endpoint này chỉ trả về khu vực email.
  - Trang dừng gọi khi mọi email đã có kết quả cuối cùng (`data-final="true"`) hoặc khi phiên đăng nhập hết hạn.
  - Nút **Làm mới** tải lại ngay.

## Thay đổi

| Lớp | File |
| --- | --- |
| Database | `database/migrations/024_ReservationEmailStatus.sql` (mới) |
| Model | `Models/Reservations/ReservationEmailStatus.cs` (mới): `EmailDeliveryState`, `ReservationEmailRecord`, `ReservationEmailItem`, `ReservationEmailPanelViewModel`, `ReservationDetailsViewModel`, `ReservationStatusDisplay`; `ReservationViewModels.cs`: thêm `Email`, `StatusLabel` |
| Service | `Services/Reservations/ReservationEmailStatusStore.cs` (mới), đăng ký trong `Program.cs` |
| Controller | `Controllers/Reservations/ReservationsController.cs`: `Details` trả về `ReservationDetailsViewModel`; thêm action `EmailStatus` |
| View | `Views/Reservations/Details.cshtml`, `_EmailStatus.cshtml` (mới); `Index.cshtml` hiển thị nhãn trạng thái đặt bàn tiếng Việt |
| JS/CSS | `wwwroot/js/reservation-email-status.js`, `wwwroot/css/reservation-email.css` (mới) |

## Demo

```powershell
cd D:\HeThongDatBan-GoiMon-Chinh
dotnet run --project tools/RestaurantManagement.DbTool -- migrate
dotnet run --project src/RestaurantManagement.Web
```

1. Tạo một lượt đặt bàn **có email** tại `/Reservations/Create`. Hệ thống tự xếp email `BookingReceived` vào hàng đợi.
2. Đăng nhập, mở **Danh sách đặt bàn**, bấm vào mã đặt bàn. Khu vực *Email xác nhận* hiện **Đang chờ gửi**, `0/4`, “Chưa thử gửi”.
3. Mô phỏng kết quả gửi bằng SQL (thay `@id` bằng Id của lượt đặt bàn):

   ```sql
   -- thử gửi lỗi một lần → "Đang thử gửi lại", 1/4, có lỗi
   UPDATE dbo.EmailOutbox SET Status='Pending',AttemptCount=1,LastAttemptAt=SYSUTCDATETIME(),
     NextAttemptAt=DATEADD(minute,5,SYSUTCDATETIME()),LastError=N'SMTP 421: máy chủ bận' WHERE ReservationId=@id;
   -- thất bại sau 4 lần → "Gửi thất bại", "Thất bại sau 4 lần thử"
   UPDATE dbo.EmailOutbox SET Status='Failed',AttemptCount=4,LastAttemptAt=SYSUTCDATETIME(),
     LastError=N'550 hộp thư không tồn tại' WHERE ReservationId=@id;
   -- gửi thành công → "Đã gửi thành công", lỗi biến mất
   UPDATE dbo.EmailOutbox SET Status='Sent',SentAt=SYSUTCDATETIME(),LastAttemptAt=SYSUTCDATETIME(),LastError=NULL WHERE ReservationId=@id;
   ```

   Khi trạng thái chưa phải kết quả cuối cùng, trang tự cập nhật sau tối đa 15 giây. Bạn cũng có thể bấm **Làm mới**.
4. Trong suốt quá trình, dòng *Trạng thái đặt bàn* vẫn là “Chờ xác nhận”, không đổi theo email.

## Kiểm thử

```powershell
dotnet build RestaurantManagement.sln -m:1
dotnet run --project tests/RestaurantManagement.AreaTests --no-build
dotnet run --no-build --project tools/RestaurantManagement.DbTool -- verify
```

**`ReservationEmailStatusTests`** (không cần database) kiểm tra:
- các trường hợp gửi: thành công, đang chờ, đang thử lại, đang gửi, thất bại sau 4 lần (có lỗi và không có lỗi), lỗi dài bị rút gọn, email nhắc lịch bị huỷ;
- mỗi trạng thái có nhãn riêng;
- mỗi mã đặt bàn nhận đúng email của mình, xếp mới nhất trước;
- lỗi của lượt 200/300 không xuất hiện ở lượt 100;
- việc tự cập nhật chỉ dừng khi mọi email đã có kết quả cuối cùng;
- kết quả gửi mới thay trạng thái “đang thử lại”;
- nhãn trạng thái đặt bàn khác nhãn trạng thái email.

**`ReservationEmailStatusVerification`** (`verify`, SQL Server thật, đăng nhập bằng quản lý):
- kiểm tra migration 024;
- gắn email vào các lượt đặt bàn mẫu D00001–D00005 và mở trang chi tiết của từng lượt: đã gửi, đang chờ, đang thử lại, thất bại, không có email;
- kiểm tra lỗi chỉ xuất hiện ở đúng lượt đặt bàn;
- gọi `usp_ClaimEmail` và `usp_CompleteEmail` thật, rồi kiểm tra `/Reservations/EmailStatus/{id}` chuyển từ “Đang gửi 3/4” sang “Đã gửi thành công” và lỗi cũ biến mất;
- kiểm tra trạng thái đặt bàn không đổi theo email, và mã đặt bàn không tồn tại trả về 404.

Sau khi kiểm tra, dữ liệu mẫu được khôi phục.
