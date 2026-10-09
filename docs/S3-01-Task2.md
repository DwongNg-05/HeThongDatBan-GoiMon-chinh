# S3-01 Task 2: Quét QR bàn đang phục vụ → vào chung phiên hiện tại

Khách quét QR của một bàn **đang phục vụ** được đưa vào **phiên hiện tại** của bàn, không tạo phiên mới, và thấy đầy đủ các món bàn đã gọi trước đó. Các món này có thể do nhân viên gọi giúp hoặc do khách khác cùng bàn gọi.

**AC hoàn tất:** bàn đã có phiên đang mở thì khách vào chung phiên và thấy món đã gọi trước đó.

**Chưa có:** xử lý QR bị sinh lại (ngoài việc mã cũ không dùng được), bàn đang dọn, đóng phiên sau thanh toán.

## Quy tắc (cần PO chốt)

| Tình huống khi quét QR | Kết quả |
| --- | --- |
| Bàn có phiên **đang mở** (`DiningSessions.Status = 'Open'`), do nhân viên mở hoặc khách khác mở bằng QR | Vào **chung phiên đó**: chỉ cấp thêm một phiên khách (`GuestSessions`) cho điện thoại, **không** tạo `DiningSessions` mới, không đổi trạng thái bàn |
| Điện thoại đã ở trong phiên (cookie còn hạn) quét lại | Dùng lại phiên khách cũ, không ghi gì thêm |
| Phiên của bàn đang **chờ thanh toán** | Từ chối: “Bàn đang chờ thanh toán”. Không nhận thêm khách hay món mới (thủ tục gọi món vốn đã chặn trường hợp này) |
| Bàn trống | Như Task 1: tạo phiên mới trong giờ hoạt động |

“Phiên đang mở của bàn” được xác định bằng dòng `SessionTables` của bàn chưa trả bàn (`ReleasedAt IS NULL`); mỗi bàn chỉ có tối đa một dòng như vậy (`UX_SessionTables_ActiveTable`). Mọi lần quét chạy dưới khoá nghiệp vụ chung, nên hai khách quét cùng lúc vẫn chỉ có một phiên.

Câu hỏi cho PO:
1. Có cần nhân viên **duyệt** khách mới vào phiên đang mở không? Hiện tại ai quét được QR dán trên bàn đều vào được.
2. Khách vào chung phiên có được thấy **giá và tạm tính** các món người khác gọi không? Hiện tại là có.

## Trang gọi món

- Khi vừa vào chung phiên, khách thấy thông báo “Bàn đang được phục vụ: bạn đã vào chung phiên gọi món của bàn…”.
- **Món đang chọn** (nhãn *Chưa gửi bếp*, khung viền nét đứt) là giỏ của riêng điện thoại này, chỉ gửi bếp khi bấm **Đặt món**.
- **Món bàn đã gọi** (*Đã gửi bếp*) là toàn bộ món của phiên, chia theo từng lượt gọi. Mỗi lượt có nhãn người gọi: **Bạn gọi** / **Khách cùng bàn gọi** / **Nhân viên gọi**, kèm số lượng, ghi chú, trạng thái bếp và tạm tính.
- Món khách gọi thêm được ghi vào **đúng phiên chung**, nên bếp, thu ngân và các điện thoại khác cùng bàn đều thấy.

## Thay đổi

| Lớp | File |
| --- | --- |
| Database | `database/migrations/045_S301JoinOpenTableSession.sql`: `usp_StartQrGuestSession` cho vào chung phiên (`Joined`), chặn khi chờ thanh toán (`AwaitingPayment`) |
| Service | `GuestTableSessionService.cs`: kết quả `Joined`/`AwaitingPayment`; `GuestOrderingContext.GuestSessionId`; món đã gọi kèm người gọi (`OrderSource`) |
| Controller | `TableQrController.cs` (thông báo vào chung phiên), `TableOrderController.cs` |
| View/CSS | `Views/TableOrder/Index.cshtml`, `wwwroot/css/table-order.css`; `TableQrUnavailableViewModel.cs` (thông báo chờ thanh toán) |
| Test | `TableQrSessionTests.cs`; `QrGuestSessionVerification.cs` (`JoinOpenSession`), chạy bằng `verify-qr-session` / `verify` |

## Demo

1. Chạy `migrate` để áp dụng `045_S301JoinOpenTableSession.sql`, rồi build và chạy web.
2. **Phục vụ** đón khách ở một bàn trống (hoặc khách thứ nhất quét QR bàn trống) và gọi vài món.
3. **Điện thoại A** (chưa đăng nhập, hoặc cửa sổ ẩn danh) quét QR của bàn: vào trang gọi món, thấy thông báo vào chung phiên và các món đã gọi trong mục **Món bàn đã gọi**. A chọn thêm món và bấm **Đặt món**.
4. **Điện thoại B** quét cùng QR: thấy đủ món của nhân viên và của A (nhãn *Khách cùng bàn gọi*).
5. Kiểm tra trong SSMS, kết quả phải là 1 phiên, 2 phiên khách:

```sql
SELECT st.TableId, st.SessionId, (SELECT COUNT(*) FROM dbo.GuestSessions g WHERE g.SessionId = st.SessionId) AS Phones
FROM dbo.SessionTables st JOIN dbo.DiningTables t ON t.Id = st.TableId
WHERE t.Code = 'A05' AND st.ReleasedAt IS NULL;
```

## Kiểm thử

`verify-qr-session` (và `verify`) chạy các kịch bản:

1. Nhân viên mở phiên và gọi 3 phần món.
2. Khách 1 quét QR: vào trang, thấy thông báo vào chung phiên, thấy món nhân viên gọi (nhãn *Nhân viên gọi*); “Món đang chọn / Chưa gửi bếp” tách riêng với “Món bàn đã gọi”. Khách 1 gọi thêm 2 phần.
3. Khách 2 quét cùng QR: thấy đủ 2 món, có nhãn *Khách cùng bàn gọi*.
4. Database: số phiên không tăng, bàn có đúng 1 phiên, 2 phiên khách, 2 lượt gọi đều thuộc phiên đó.
5. Quét lần tiếp theo không tạo thêm gì.
6. Bàn chờ thanh toán: khách mới bị từ chối và không ghi gì.

Ngoài ra, kịch bản nhiều người quét cùng lúc của Task 1 nay cũng kiểm rằng quét sau 120 giây vẫn vào chung phiên.
