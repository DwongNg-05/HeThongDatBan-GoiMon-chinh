# S3-01 Task 3: Chặn mở phiên khi QR đã bị thay thế hoặc bàn đang dọn

Khách quét **mã QR cũ** (đã bị nhà hàng sinh lại) hoặc quét QR của **bàn đang dọn** sẽ không vào được trang gọi món. Thay vào đó, khách thấy thông báo rõ ràng kèm hướng dẫn gọi phục vụ. Hệ thống không tạo phiên phục vụ hay phiên khách nào.

**AC hoàn tất:** QR đã sinh lại và bàn đang dọn đều bị chặn, khách được đề nghị gọi phục vụ.

**Chưa có:** tự đóng phiên khi thanh toán; cho QR cũ mở phiên mới sau thanh toán.

## Nội dung thông báo (đề xuất, cần PO chốt)

| Tình huống | HTTP | Tiêu đề | Nội dung |
| --- | --- | --- | --- |
| Mã QR đã bị sinh lại | 410 | **Mã QR đã thay đổi** | “Mã QR đã thay đổi. Vui lòng gọi phục vụ.” + “Nhà hàng đã thay mã QR mới cho bàn này nên mã bạn vừa quét không còn dùng để gọi món.” |
| Bàn đang dọn | 409 | **Bàn đang được dọn** | “Bàn đang được dọn dẹp để đón khách mới nên chưa mở được phiên gọi món. Vui lòng chờ ít phút hoặc gọi phục vụ để được sắp xếp bàn.” |
| Mã không tồn tại / bàn ngừng dùng | 404 | Không tìm thấy mã QR hợp lệ | “Mã QR này không thuộc bàn nào đang sử dụng.” |

Mọi trang trên (và các trang từ chối khác: bàn đã đặt trước, chờ thanh toán, ngoài giờ) đều có khối **“Vui lòng gọi phục vụ để được hỗ trợ”**:

1. Giơ tay hoặc báo nhân viên gần nhất.
2. Đọc **mã bàn** (ví dụ **A05**) cho nhân viên. Mã bàn chỉ hiện khi đã biết bàn, tức là không có ở trường hợp mã không tồn tại.
3. Nhân viên sẽ mở hoặc sắp xếp bàn giúp, khách không cần đăng nhập hay cài ứng dụng.

Kèm nút **“Gọi nhà hàng 0xxxxxxxxx”** (`tel:`) lấy từ `RestaurantSettings.Phone`. Nút chỉ hiện khi số điện thoại hợp lệ (10 chữ số). Trước khi demo, cập nhật số thật:
`UPDATE dbo.RestaurantSettings SET Phone='0xxxxxxxxx' WHERE Id=1;`

Câu hỏi cho PO:
1. Có giữ nút gọi điện cho nhà hàng không, hay chỉ hướng dẫn báo nhân viên tại bàn?
2. Có cần thêm nút “Xem thực đơn” trên các trang này không? Hiện tại chưa có, để khách tập trung vào việc gọi phục vụ.

## Cách hoạt động

- **Phiên bản mã QR:** cột mới `TableQrCodes.Version` (1, 2, 3… theo từng bàn). Mã cũ được đánh số lại theo thứ tự tạo. `usp_RotateTableQr` ghi phiên bản kế tiếp và thu hồi mã cũ trong cùng giao dịch (S1-07). Chỉ mục `UX_TableQrCodes_Active` bảo đảm mỗi bàn có đúng một mã hiện tại, `UX_TableQrCodes_TableVersion` bảo đảm phiên bản không trùng.
- **Đối chiếu khi quét:** mã khách quét được tra bằng SHA-256 (`TokenHash`). Nếu mã đó không còn là mã hiện tại của bàn (đã thu hồi) thì trả về `QrChanged`. Việc kiểm tra diễn ra cả ở trang mở đường dẫn (GET) lẫn trong thủ tục `usp_StartQrGuestSession` (POST), nên gửi thẳng yêu cầu mở phiên bằng mã cũ cũng bị chặn. Khi sinh lại mã, các phiên khách đang dùng mã cũ cũng bị thu hồi (S1-07), nên điện thoại đó không mở lại trang gọi món được nữa.
- **Bàn đang dọn:** trang GET kiểm tra trạng thái bàn trước: bàn `Cleaning` thì hiện thông báo ngay, không có biểu mẫu tự mở phiên. Thủ tục SQL (migration 046) kiểm tra `Cleaning` ngay sau khi kiểm mã, trước cả bước “quét lại” và “vào chung phiên”, trong cùng giao dịch và dưới khoá nghiệp vụ chung.
- **Không ghi gì khi bị từ chối:** không có `DiningSessions`, `SessionTables` hay `GuestSessions` mới, trạng thái bàn giữ nguyên.

## Thay đổi

| Lớp | File |
| --- | --- |
| Database | `database/migrations/046_S301QrVersionAndCleaning.sql`: `TableQrCodes.Version`, `usp_RotateTableQr` ghi phiên bản, `usp_StartQrGuestSession` chặn bàn đang dọn sớm |
| Service | `TableQrService.cs`: tra mã trả thêm trạng thái bàn và phiên bản (`IsReplaced`, `IsCleaning`), đọc số điện thoại nhà hàng |
| Controller | `TableQrController.cs`: GET chặn bàn đang dọn; mọi trang từ chối có hướng dẫn gọi phục vụ |
| Model/View | `CallStaffViewModel.cs`, `Views/TableQr/_CallStaff.cshtml` (mới); `Changed.cshtml`, `NotFound.cshtml`, `Unavailable.cshtml`, `TableQrUnavailableViewModel.cs` |
| Test | `TableQrSessionTests.cs`; `QrGuestSessionVerification.cs` (`BlockedScans`) |

## Demo

1. Chạy `migrate` để áp dụng `046_S301QrVersionAndCleaning.sql`, rồi build và chạy web.
2. **QR cũ:** Quản lý mở **Khu vực & bàn → bàn → Mở thử QR** và chép đường dẫn. Bấm **Đổi mã QR mới**. Mở đường dẫn cũ trong cửa sổ ẩn danh: thấy “Mã QR đã thay đổi” kèm hướng dẫn gọi phục vụ và mã bàn, không vào trang gọi món.
3. **Bàn đang dọn:** chuyển một bàn sang **Đang dọn** (trên Sơ đồ bàn, hoặc sau khi thanh toán). Quét QR hiện tại của bàn: thấy “Bàn đang được dọn” kèm hướng dẫn gọi phục vụ.
4. Kiểm tra trong SSMS: không có phiên mới cho hai bàn trên.

```sql
SELECT t.Code, q.Version, q.RevokedAt FROM dbo.TableQrCodes q JOIN dbo.DiningTables t ON t.Id = q.TableId
WHERE t.Code = 'A05' ORDER BY q.Version;   -- phiên bản cũ có RevokedAt, phiên bản lớn nhất là mã hiện tại
```

## Kiểm thử

Lệnh `verify-qr-session` (và `verify`) chạy hai nhóm kịch bản.

**QR cũ:**
- Khách mở bàn bằng mã phiên bản 1, sau đó mã được sinh lại thành phiên bản 2.
- Mở hoặc gửi thẳng mã cũ đều nhận 410 kèm hướng dẫn gọi phục vụ (mã bàn, nút gọi nhà hàng) và không có biểu mẫu mở phiên.
- Điện thoại từng dùng mã cũ không mở lại trang gọi món được.

**Bàn đang dọn:**
- Mở đường dẫn hay gửi thẳng yêu cầu đều nhận 409 kèm hướng dẫn gọi phục vụ.
- Không tạo phiên hay phiên khách nào, bàn vẫn ở trạng thái đang dọn.
