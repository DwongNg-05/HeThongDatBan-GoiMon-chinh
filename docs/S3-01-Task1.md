# S3-01 Task 1: Quét QR bàn trống → vào phiên gọi món mới

Khách quét mã QR hợp lệ của một bàn đang trống và được đưa ngay vào trang gọi món của đúng bàn đó. Hệ thống tạo một phiên phục vụ mới và chuyển bàn sang “Đang phục vụ”. Khách không cần đăng nhập hay cài ứng dụng.

**AC hoàn tất:**
- QR hợp lệ mở đúng bàn, không cần đăng nhập hay cài ứng dụng.
- Bàn trống được tạo phiên mới và chuyển sang “Đang phục vụ”.

**Chưa có trong task này:** vào chung phiên đang mở, kiểm soát QR bị sinh lại (ngoài việc mã cũ không dùng được), trạng thái đang dọn bàn, đóng phiên sau thanh toán, chọn món và gửi bếp từ điện thoại khách.

## Quy tắc cần PO chốt

Các quy tắc dưới đây đã được cài đặt theo đề xuất. Nếu PO chọn khác, chỉ cần sửa `dbo.usp_StartQrGuestSession` (migration mới), không phải sửa giao diện.

### 1. Khi nào bàn được coi là “trống”

Quét QR chỉ mở phiên mới khi **tất cả** điều kiện sau đúng tại thời điểm máy chủ xử lý:

| # | Điều kiện | Khi không đạt, khách thấy |
| --- | --- | --- |
| 1 | Mã QR tồn tại, chưa bị sinh lại; bàn và khu vực đang sử dụng | “Không tìm thấy mã QR hợp lệ” (404) hoặc “Mã QR đã thay đổi” (410) |
| 2 | Trạng thái bàn là **Trống** (`Available`) | Đã đặt trước / Đang phục vụ / Đang dọn (409) |
| 3 | Bàn không gắn với phiên phục vụ nào chưa đóng, kể cả phiên đang chờ thanh toán | “Bàn đang được phục vụ” (409) |
| 4 | Không có lượt đặt *Chờ xác nhận* hoặc *Đã xác nhận* gán cho bàn mà giờ bắt đầu rơi vào **một lượt ngồi tới** (`RestaurantSettings.DefaultBookingMinutes`, mặc định 90 phút), hoặc đang trong giờ đặt cộng 15 phút dọn bàn (cùng quy tắc giữ bàn của S2-03) | “Bàn đã được đặt trước” (409) |
| 5 | Đang trong **giờ hoạt động** hôm nay (giờ Việt Nam, màn hình **Giờ hoạt động**: giờ mở cửa ≤ hiện tại < giờ đóng cửa, ngày không nghỉ, không phải ngày nghỉ đặc biệt). **Không** bắt buộc thu ngân mở ca trước (migration 044): có ca đang mở thì phiên gắn vào ca đó; chưa có thì phiên được gắn khi thu ngân mở ca | “Nhà hàng đang ngoài giờ hoạt động” (409) |

Mọi trường hợp bị từ chối **không ghi gì** vào database và luôn hướng khách gọi nhân viên.

### 2. Thời điểm bàn chuyển sang “Đang phục vụ”

Bàn chuyển `Available → Serving` **đúng lúc phiên phục vụ được tạo thành công**. Việc tạo phiên, gắn bàn, đổi trạng thái bàn và cấp phiên cho điện thoại khách nằm trong cùng một giao dịch SQL: hoặc thành công cả bốn, hoặc không có gì thay đổi.

- Bàn **không** chuyển trạng thái khi chỉ mở đường dẫn QR (GET). Các ứng dụng chat như Zalo hay Messenger thường tự tải trước đường dẫn để tạo bản xem trước; nếu mở link mà đã đổi trạng thái thì bàn có thể bị mở nhầm. Trình duyệt của khách tự gửi yêu cầu mở phiên (POST) ngay sau khi trang hiện ra, nên khách không phải bấm gì thêm.
- Bàn cũng **không** đợi tới lúc khách gửi món đầu tiên mới chuyển trạng thái.
- Trigger sẵn có ghi sự kiện `Available → Serving`, nên sơ đồ bàn của nhân viên tự cập nhật.

### 3. Quét lặp trong thời gian ngắn

| Tình huống | Kết quả |
| --- | --- |
| Cùng điện thoại quét lại (cookie phiên khách còn hạn) | Vào lại đúng phiên, không ghi gì mới |
| Bấm nhiều lần, mạng chậm gửi lại, hoặc cả nhóm quét cùng lúc **trong 120 giây** sau khi chính mã này mở bàn, khi phiên **chưa có món** | Vào đúng phiên vừa mở. Không tạo phiên thứ hai, không đổi trạng thái thêm lần nào |
| Điện thoại khác quét khi bàn đã phục vụ lâu hơn 120 giây hoặc đã có món | “Bàn đang được phục vụ, vui lòng gọi nhân viên”. Phần vào chung phiên làm ở task sau |

Các yêu cầu được xếp hàng bằng khoá nghiệp vụ chung (`usp_LockOperations`), nên dù 8 yêu cầu tới cùng lúc vẫn chỉ có đúng một phiên.

### 4. Câu hỏi còn mở cho PO

1. **Số khách** của phiên mở bằng QR tạm ghi là 1. Nhân viên sẽ sửa sau, hoặc hỏi khách ở bước sau?
2. Trong giờ hoạt động mà **chưa mở ca** thì vẫn cho khách mở phiên; phiên được gắn vào ca khi thu ngân mở ca, và phải có ca mới thanh toán được. PO đồng ý cách này chưa?
3. **Cửa sổ quét lặp 120 giây** và **thời hạn phiên khách 12 giờ** (bằng `usp_OpenGuestSession` có sẵn) đã phù hợp chưa?
4. Bàn có lượt đặt trong **90 phút tới** thì không cho khách vãng lai tự mở. PO có muốn khoảng thời gian khác không?

## Cách hoạt động

```
Điện thoại quét QR ──GET /q/{mã}──► kiểm tra mã, hiện "Bạn đang ở bàn A05" (không ghi gì)
          │  trình duyệt tự gửi biểu mẫu (table-qr-start.js), có mã chống giả mạo
          └─POST /q/{mã}─► dbo.usp_StartQrGuestSession (1 giao dịch, khoá nghiệp vụ)
                              ├─ Started / Rejoined / Resumed ─► cookie RM.TableSession ─► 302 /TableOrder
                              └─ bàn không trống / ngoài giờ ────► trang "gọi nhân viên" (409)
GET /TableOrder ─► đọc phiên khách từ cookie ─► "Bàn A05 · Tầng một" + thực đơn đang bán
```

- **Dữ liệu nhận diện QR:** mỗi bàn có đúng một mã QR đang hiệu lực (`TableQrCodes`, chỉ mục duy nhất theo bàn). Mã công khai là 24 byte ngẫu nhiên, mã hoá base64url (S1-07); database tra cứu bằng SHA-256 của mã (`TokenHash`, duy nhất). Bàn cũ chưa có mã thì Quản lý bấm **Tạo mã QR**.
- **Đường dẫn trong QR:** `{TableQr:PublicBaseUrl}/q/{mã}`, giữ nguyên định dạng nên QR đã in vẫn dùng được.
- **Phiên khách:** điện thoại nhận một mã ngẫu nhiên 32 byte trong cookie `RM.TableSession` (HttpOnly, SameSite=Lax, Secure khi chạy HTTPS, hết hạn sau 12 giờ). Database chỉ lưu SHA-256 của mã này (`GuestSessions.TokenHash`). Khi Quản lý sinh lại QR, các phiên khách của bàn bị thu hồi (S1-07).
- **Phiên phục vụ do khách mở:** `DiningSessions.OpenedBy` để trống, `OpenedByQrCodeId` ghi mã QR đã dùng. Ràng buộc `CK_DiningSessions_Opener` bảo đảm mỗi phiên có đúng một nguồn mở: nhân viên hoặc QR.
- **Trang gọi món** `/TableOrder`: hiển thị mã bàn cỡ lớn, khu vực, loại bàn, sức chứa, giờ mở phiên, cùng thực đơn đang bán (nhãn “Tạm hết” tự cập nhật như `/Menu`). Trang không lưu đệm (`Cache-Control: no-store`). Chưa quét QR, phiên hết hạn hoặc QR đã đổi thì trang hướng dẫn khách quét mã trên bàn.
- **Nhân viên đang đăng nhập** bấm **Mở thử QR**: trang **không** tự mở phiên mà hiện cảnh báo và nút “Bắt đầu gọi món”, để tránh vô tình chuyển bàn sang “Đang phục vụ”.

## Thay đổi

| Lớp | File |
| --- | --- |
| Database | `database/migrations/043_S301QrGuestSession.sql` (mới): `OpenedBy` cho phép NULL, thêm `OpenedByQrCodeId`, `CK_DiningSessions_Opener`, `usp_StartQrGuestSession`; `044_S301QrOpeningHours.sql`: theo giờ hoạt động thay cho ca (`fn_IsWithinOpeningHours`), `ShiftId` được để trống với phiên QR, `usp_OpenShift` gắn phiên QR vào ca mới |
| Service | `Services/Tables/GuestTableSessionService.cs` (mới); `TableQrService.Hash/IsValidPublicToken` chuyển sang public; đăng ký trong `Program.cs` |
| Controller | `Controllers/Tables/TableQrController.cs` (thêm `POST /q/{mã}`), `Controllers/Tables/TableOrderController.cs` (mới, `GET /TableOrder`) |
| Model | `Models/Tables/TableQrScanViewModel.cs` (`Token`, `AutoStart`), `TableQrUnavailableViewModel.cs`, `TableOrderViewModel.cs` (mới) |
| View/JS/CSS | `Views/TableQr/Scan.cshtml`, `Views/TableQr/Unavailable.cshtml`, `Views/TableOrder/Index.cshtml`, `Views/TableOrder/NoSession.cshtml`, `wwwroot/js/table-qr-start.js`, `wwwroot/css/table-order.css` |
| Phân quyền | `tools/.../ApiAccessMatrix.cs`: `TableQr.Start POST` và `TableOrder.Index GET` là API công khai |
| Test | `tests/.../TableQrSessionTests.cs`, `tools/.../QrGuestSessionVerification.cs`, lệnh `verify-qr-session` |

## Demo

```powershell
cd D:\HeThongDatBan-GoiMon-Chinh
$env:RM_CONNECTION_STRING = 'Server=.\MSSQLSERVER07;Database=RestaurantManagement_Dev;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True'
dotnet build RestaurantManagement.sln --no-restore
dotnet run --project tools/RestaurantManagement.DbTool -- migrate      # áp dụng 043 và 044
dotnet run --project src/RestaurantManagement.Web --launch-profile https
```

Muốn quét bằng điện thoại thật thì đặt `TableQr__PublicBaseUrl` thành địa chỉ HTTPS mà điện thoại truy cập được, giống hướng dẫn S1-07 trong README. Không commit địa chỉ IP.

1. Kiểm tra màn hình **Giờ hoạt động**: giờ hiện tại phải nằm trong giờ mở cửa của hôm nay. Không cần mở ca trước; muốn thanh toán thì thu ngân mở ca tại **Chốt ca** (`/Cashier/Shift`).
2. **Quản lý** vào **Khu vực & bàn**, chọn một bàn đang **Trống** và chưa có lượt đặt sắp tới. Bấm **Tạo mã QR** nếu bàn chưa có mã, rồi **Tải ảnh QR để in**.
3. Dùng điện thoại **chưa đăng nhập** quét mã: thoáng thấy “Bạn đang ở bàn …” rồi vào ngay trang **Gọi món tại bàn**, có mã bàn cỡ lớn, khu vực và thực đơn.
4. Trên máy nhân viên, **Sơ đồ bàn** hiện bàn đó là **Đang phục vụ** (tự cập nhật, không cần tải lại).
5. Quét lại hoặc bấm “Quay lại” rồi quét tiếp: vẫn vào đúng trang, **không** có phiên mới. Kiểm tra trong SSMS:

```sql
SELECT t.Code, t.Status, s.Id AS SessionId, s.OpenedAt, s.OpenedBy, s.OpenedByQrCodeId,
       (SELECT COUNT(*) FROM dbo.GuestSessions g WHERE g.SessionId = s.Id) AS GuestPhones
FROM dbo.DiningTables t
JOIN dbo.SessionTables st ON st.TableId = t.Id AND st.ReleasedAt IS NULL
JOIN dbo.DiningSessions s ON s.Id = st.SessionId
WHERE t.Code = 'A05';   -- mã bàn vừa quét: đúng 1 dòng, Status = Serving, OpenedBy = NULL
```

6. Quét QR của một bàn **Đã đặt trước** hoặc **Đang dọn**: khách thấy thông báo gọi nhân viên, bàn giữ nguyên trạng thái.

> Không có điện thoại: trên máy tính, mở `https://localhost:7114/q/<mã>` trong cửa sổ ẩn danh (chưa đăng nhập). Lấy mã từ nút **Mở thử QR**.

## Kiểm thử

```powershell
dotnet build RestaurantManagement.sln --no-restore
dotnet run --no-build --project tests/RestaurantManagement.AreaTests          # không cần database
dotnet run --no-build --project tools/RestaurantManagement.DbTool -- verify-qr-session   # SQL Server + web thật, database tạm
dotnet run --no-build --project tools/RestaurantManagement.DbTool -- verify             # toàn bộ, đã gồm phần trên
```

| Nhóm | Nội dung kiểm |
| --- | --- |
| QR hợp lệ của bàn trống | Mở link (GET) **không** tạo phiên; POST chuyển tới `/TableOrder`; tạo đúng 1 phiên của đúng bàn, `OpenedBy` NULL, `OpenedByQrCodeId` đúng mã; 1 phiên khách |
| Bàn chuyển “Đang phục vụ” | `Status = 'Serving'`, có sự kiện `Available → Serving` cho sơ đồ bàn |
| Trang gọi món không đăng nhập | 200, có mã bàn và khu vực, có món kèm giá, không có menu nhân viên, không tạo cookie đăng nhập, `no-store` |
| Quét nhiều lần | Cùng điện thoại quét lại: không ghi thêm. **8 yêu cầu đồng thời** (5 điện thoại + 1 điện thoại bấm 3 lần): tất cả vào trang gọi món, đúng 1 phiên, đúng 1 lần đổi trạng thái. Quá 120 giây: điện thoại mới bị từ chối, điện thoại cũ vẫn vào lại được |
| Bàn không trống | Đã đặt trước, đang dọn, có lượt đặt sau 30 phút, ngoài giờ hoạt động: 409. Trong giờ hoạt động nhưng chưa mở ca vẫn mở được phiên, mở ca sau thì phiên gắn vào ca, không tạo phiên, giữ trạng thái |
| Mã sai / đã đổi | 404 / 410; sau khi sinh lại QR, cookie cũ không mở được trang gọi món |
| Không cần database | Định dạng và độ ngẫu nhiên của mã phiên khách, thuộc tính cookie, thông báo theo từng kết quả, mã sai bị chặn trước khi tới database, phân quyền công khai của 2 API mới |

## Bổ sung: nút “Đặt món” và danh sách “Món đã đặt”

Trên trang `/TableOrder`, mỗi món có nút **+ Thêm vào giỏ** (bị khoá khi món đang “Tạm hết”).

- **Giỏ món** dính ở cuối màn hình, cho phép tăng/giảm số lượng (1–99), bỏ món và ghi chú tối đa 200 ký tự. Giỏ được lưu tạm trên điện thoại, nên tải lại trang không bị mất.
- Bấm **Đặt món** sẽ gửi `POST /TableOrder/Submit`, gọi thủ tục có sẵn `dbo.usp_SubmitOrder` theo đường khách (`@GuestTokenHash`). Thủ tục kiểm tra phiên khách còn hạn, phiên phục vụ đang mở, món đang bán và không tạm hết, đồng thời chụp lại giá tại thời điểm gọi. Món vào hàng đợi của bếp như món nhân viên gọi.
- Mỗi lần hiển thị trang có một mã yêu cầu (`requestId`), nên bấm “Đặt món” nhiều lần hoặc mạng gửi lại vẫn chỉ tạo **một** lượt gọi.
- **Món đã đặt** liệt kê các món theo từng lượt gọi (giờ gọi, số lượng, ghi chú, trạng thái bếp: Chờ bếp nhận / Đang nấu / Đã xong, chờ mang ra / Đã phục vụ / Đã huỷ) và tạm tính. Danh sách gồm cả món nhân viên gọi giúp bàn.
- Bàn đang chờ thanh toán, phiên hết hạn hoặc món vừa hết: khách thấy thông báo, giỏ được giữ nguyên để sửa rồi đặt lại.
