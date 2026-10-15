# Gộp hai bàn — Task 1

## Quy tắc nghiệp vụ chính thức

Người dùng đã xác nhận sử dụng các quy tắc dưới đây làm nghiệp vụ chính thức cho Task 1 trong cuộc trao đổi ngày 15/10/2026. Đây là xác nhận của người dùng trong dự án; không ghi nhận một cuộc họp hay phê duyệt riêng từ người khác.

1. Hai bàn liền nhau khi quản lý khai báo cặp bàn tại **Quản lý bàn → Cấu hình bàn liền nhau**. Cặp có tính hai chiều, chỉ khai báo hai bàn khác nhau cùng khu vực đang hoạt động. Không suy từ tên/số bàn hay khoảng cách tọa độ chưa có quy ước.
2. Phục vụ chọn bàn giữ phiên chính trong bước xác nhận. Có thể giữ bàn đang mở hoặc bàn ghép. Chỉ gộp đúng hai bàn độc lập; không ghép tiếp một nhóm đã gộp.
3. Món gọi thêm có bàn gốc là bàn mở màn hình gọi món. Với khách dùng QR, bàn gốc là bàn của phiên khách gắn với QR. Dù gọi từ bàn phụ, đợt món mới thuộc phiên chính.
4. Hai bàn phải cùng ca đang mở. Cả hai còn đang phục vụ, phiên `Open`, chưa thuộc nhóm và chưa có hoá đơn. Món huỷ không tính tiền được loại khỏi tổng; món huỷ có tính tiền vẫn theo quy tắc hiện hành.
5. Khi thanh toán xong, cả nhóm chuyển **Đang dọn**; chỉ về **Trống** sau thao tác báo dọn xong hiện có.

## Đối chiếu từng yêu cầu

| Yêu cầu | Hiện thực |
|---|---|
| Chốt cách xác định liền nhau và chọn phiên chính | Đã được người dùng xác nhận: quản lý khai báo cặp liền nhau, phục vụ chọn bàn chính |
| Chốt bàn gốc cho món gọi thêm | Đã được người dùng xác nhận: bàn đang mở / bàn của QR, áp dụng cả đường nhân viên và khách |
| Chọn bàn đang phục vụ, bàn ghép, xác nhận | Sơ đồ → mở bàn → mục Gộp bàn → chọn bàn ghép → chọn bàn chính → tích xác nhận → Xác nhận gộp bàn |
| Liền nhau, cùng khu vực, đang phục vụ, chưa chờ thanh toán | `usp_MergeSessions` kiểm tra lại bên trong giao dịch, không tin danh sách trên trình duyệt |
| Lưu nhóm, phiên chính, bàn gốc cố định | `SessionMerges`, `DiningSessions.BillingSessionId`, `SessionTables`, `OrderItems.OriginalTableId`; trigger chặn sửa bàn gốc |
| Chuyển toàn bộ dòng món nguyên vẹn một lần | Chuyển quyền sở hữu `OrderBatches.SessionId` sang phiên chính trong cùng giao dịch; không sao chép dòng món; giữ ID dòng, số lượng, đơn giá, ghi chú, trạng thái và bàn gốc |
| Không trùng số đợt | Đánh lại số đợt của bàn phụ nối tiếp số đợt bàn chính; giữ nguyên ID đợt và mã yêu cầu |
| Mở bất kỳ bàn nào thấy món chung, bàn gốc | Trang gọi món của phục vụ, trang khách dùng QR và dữ liệu chi tiết bàn đọc cùng phiên chính; thêm cột/nhãn Bàn gốc |
| Món mới vào phiên chính, đúng nguồn | `usp_SubmitOrder` lấy phiên nguồn để xác định bàn gốc, lấy phiên chính để lưu đợt món; `OriginSessionId` giữ nguồn và chống gửi lặp sau khi chuyển đợt |
| Một hoá đơn và tổng chung | Dùng luồng hoá đơn tạm tính, thu ngân và `usp_Checkout` hiện có theo phiên chính; từng dòng giữ mã bàn gốc trên hoá đơn |
| Trạng thái cả nhóm | Nút Yêu cầu thanh toán cả nhóm bàn chuyển cả hai phiên sang `AwaitingPayment`; thanh toán đóng cả hai phiên, thu hồi phiên khách và đưa cả hai bàn sang `Cleaning` |
| Gộp trùng / trạng thái vừa đổi | Khoá nghiệp vụ chung, kiểm tra phiên bản `rowversion` của hai phiên trước khi ghi; từ chối bằng thông báo và chuyển về màn hình bàn để đọc lại dữ liệu, lựa chọn và phiên bản mới |
| Hai yêu cầu gộp đồng thời | Chỉ một giao dịch thành công, giao dịch còn lại bị từ chối; không có vòng tham chiếu phiên chính |

Phạm vi demo tương ứng AC 1 và AC 2 theo mô tả đã cung cấp. Chưa có văn bản AC riêng để đối chiếu thêm. Không thêm nhãn nhóm trên sơ đồ và không thêm thao tác huỷ gộp. Thủ tục huỷ gộp cũ được bảo vệ để không tách phiên đã chuyển món, tránh làm mất món của bàn phụ khỏi hoá đơn.

## Cập nhật cơ sở dữ liệu và chạy

Migration mới: `database/migrations/059_MergeTablesTask1.sql`. Không sửa các migration cũ đã áp dụng. Migration bổ sung bảng cặp liền nhau, nguồn phiên của đợt món, chuyển các đợt món của nhóm cũ về phiên chính và cập nhật thủ tục. Đơn giá, số lượng, bàn gốc và dòng hoá đơn đã chốt không bị sửa.

Trong PowerShell tại thư mục dự án, dùng chuỗi kết nối đúng máy:

```powershell
$env:RM_CONNECTION_STRING = 'Server=lpc:.\SQLEXPRESS;Database=RestaurantManagement_Dev;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True'
dotnet run --project tools/RestaurantManagement.DbTool -- migrate
dotnet run --project src/RestaurantManagement.Web
```

Ngày 15/10/2026, theo yêu cầu người dùng, đã áp dụng đủ 18 migration còn thiếu vào `RestaurantManagement_Dev` trên `lpc:.\SQLEXPRESS`, gồm migration 059. Kiểm tra sau cập nhật: 0 migration còn thiếu, 0 migration đã áp dụng bị đổi nội dung; các cấu trúc `QuickNotes`, `OriginSessionId` và `TableAdjacencies` đã có. Kiểm thử tạo món/gộp/thanh toán vẫn dùng database thử riêng. Không tự khai báo cặp liền nhau trong dữ liệu sử dụng vì quản lý cần xác nhận vị trí thực tế.

## Demo

1. Quản lý mở **Quản lý bàn → Cấu hình bàn liền nhau**, khai báo hai bàn thực sự liền nhau trong cùng khu vực.
2. Có ca đang mở, đón khách/mở phiên phục vụ cho từng bàn bằng luồng hiện có. Gọi vài món từ từng bàn, có thể gọi cùng món để kiểm tra các dòng vẫn riêng biệt.
3. Phục vụ chọn bàn thứ nhất trên sơ đồ. Trong màn hình gọi món, mở **Gộp bàn**, chọn bàn thứ hai, chọn bàn giữ phiên chính và tích xác nhận.
4. Xác nhận gộp. Mở lần lượt cả hai bàn: mỗi bên đều có cùng danh sách món, tạm tính và cột Bàn gốc. Liên kết hoá đơn chung trỏ đến cùng phiên chính.
5. Gọi thêm từ từng bàn hoặc QR bàn phụ. Món vào danh sách chung, bàn gốc vẫn là bàn gọi thêm.
6. Bếp chế biến và phục vụ mang ra theo luồng hiện có. Phục vụ chọn **Yêu cầu thanh toán cả nhóm bàn** từ một trong hai bàn.
7. Thu ngân thấy một mục của phiên chính kèm cả hai mã bàn, thanh toán một lần. Kiểm tra một hoá đơn, tổng chính xác, cả hai bàn sang Đang dọn, phiên QR cũ kết thúc.
8. Kiểm tra từ chối: hai bàn chưa khai báo liền nhau, khác khu vực, không phục vụ, đã chờ thanh toán; thay đổi trạng thái ở cửa sổ khác trong khi đang xác nhận; gửi hai yêu cầu cùng lúc.

## Kiểm thử đã chạy

```powershell
dotnet build RestaurantManagement.sln -m:1
dotnet tests/RestaurantManagement.AreaTests/bin/Debug/net10.0/RestaurantManagement.AreaTests.dll
dotnet tools/RestaurantManagement.DbTool/bin/Debug/net10.0/RestaurantManagement.DbTool.dll verify-merge-tables
dotnet tools/RestaurantManagement.DbTool/bin/Debug/net10.0/RestaurantManagement.DbTool.dll verify-merge-upgrade
```

- Build thành công với kiểm tra bảo mật bật. Đã nâng `System.Security.Cryptography.Pkcs` lên `10.0.13` thống nhất toàn solution để xử lý NU1902/NU1903. Còn cảnh báo giấy phép ImageSharp có sẵn.
- Bộ hồi quy: **799 kiểm thử đạt** (chạy với quyền mở kết nối HTTP/SMTP cục bộ).
- `verify-merge-tables` chạy SQL Server thật và web thật trên cơ sở dữ liệu mới có tên duy nhất, áp migration hai lần để kiểm tra chạy lại an toàn, rồi xoá cơ sở dữ liệu thử sau khi xong.
- `verify-merge-upgrade` tái hiện schema cũ chưa có `QuickNotes`, tạo hai bàn có món và nhóm đã gộp bằng thủ tục cũ, rồi nâng cấp. Đã xác nhận giữ ID món, giá, số lượng, ghi chú và bàn gốc; chuyển các đợt về phiên chính không trùng số; gửi lại yêu cầu cũ không thêm món; màn hình Bếp và cả hai bàn hoạt động sau nâng cấp. Trước nâng cấp, ứng dụng trả thông báo HTTP 503 cần cập nhật thay vì chạy truy vấn lỗi thiếu cột.
- Đã kiểm tra: không liền nhau; khác khu vực; bàn không phục vụ; chờ thanh toán; quyền cấu hình; chống giả mạo; phiên bản cũ sau yêu cầu thanh toán/mở lại; gộp đồng thời hai chiều; gộp lặp; không mất hoặc nhân đôi ID/dòng món; số đợt không trùng; giá, số lượng, tổng và nguồn giữ nguyên; bàn gốc không sửa được; gửi lại yêu cầu cũ sau gộp; gọi thêm từ nhân viên và QR; món chung và bàn gốc trên cả hai màn hình; cả nhóm chờ thanh toán; chặn gọi thêm; một hoá đơn và một thanh toán dù gửi thanh toán đồng thời; cả nhóm đóng, dọn bàn và thu hồi phiên QR; quản lý cấu hình qua form; phục vụ xác nhận qua form và chọn bàn ghép giữ phiên chính.
