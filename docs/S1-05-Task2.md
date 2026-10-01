# S1-05 Task 2 — Lọc nhật ký theo khoảng ngày và tài khoản

Mục tiêu: lọc nhật ký theo khoảng ngày và theo tài khoản; mở màn hình thì mặc định hiển thị 7 ngày gần nhất. Hoàn tất **AC2**.

## Quy tắc cần chốt với PO (đề xuất đã cài đặt)

| Câu hỏi | Đề xuất | Lý do |
| --- | --- | --- |
| Chọn tài khoản như thế nào? | **Chọn từ danh sách có sẵn**, không nhập tự do. Danh sách lấy từ bảng tài khoản `dbo.Users`, gồm cả tài khoản đã ngừng hoạt động (có ghi chú “ngừng hoạt động”), kèm vai trò. Có thêm mục **“Định danh không tồn tại”** để xem các lần đăng nhập bằng tên/số điện thoại không có thật. | Không gõ sai tên; lọc theo mã tài khoản nên đổi tên đăng nhập không làm mất lịch sử; vẫn xem được lịch sử của nhân viên đã nghỉ. |
| Giới hạn khoảng ngày? | **Tối đa 90 ngày**, tính cả hai đầu. “Từ ngày” không được sau “Đến ngày”. | Đủ cho rà soát theo quý; giữ truy vấn nhanh khi nhật ký lớn dần. |
| “7 ngày gần nhất” là gì? | **Hôm nay và 6 ngày trước**, theo ngày Việt Nam (UTC+7), từ 00:00 ngày đầu đến hết 23:59:59 hôm nay. | Khớp quy ước “ngày nghiệp vụ theo UTC+7” trong README. |
| Nhiều dữ liệu quá thì sao? | Hiển thị **tối đa 1.000 dòng mới nhất** và báo tổng số dòng khớp, kèm gợi ý thu hẹp bộ lọc. | Phân trang chưa thuộc phạm vi task này. |

Nếu PO chọn khác (ví dụ 31 ngày, hoặc cho nhập tự do), chỉ cần sửa các hằng số `MaxDays`, `DefaultDays`, `RowLimit` và cách lấy tài khoản trong `Models/SecurityAuditFilter.cs`.

## Thay đổi

- **Màn hình `/AuditLogs`**: thêm bộ lọc **Từ ngày**, **Đến ngày** (ô chọn ngày), **Tài khoản** (danh sách chọn), nút **Lọc** và nút **7 ngày gần nhất** để về mặc định.
  - Dòng tóm tắt cho biết số sự kiện, khoảng ngày và tài khoản đang lọc.
  - Ngày và tài khoản đã chọn được giữ lại trên form.
  - Bộ lọc dùng `GET` (`?fromDate=2026-09-01&toDate=2026-09-30&userId=2`), nên có thể lưu hoặc chia sẻ đường link; `userId=0` là “Định danh không tồn tại”.
- **Danh sách rỗng hợp lệ**: khoảng ngày không có dữ liệu hiển thị “Không có sự kiện nào khớp điều kiện lọc.”, không báo lỗi.
- **Kiểm tra dữ liệu nhập** (thông báo tiếng Việt, khi có lỗi thì không truy vấn):
  - ngày sai định dạng;
  - “Từ ngày” sau “Đến ngày”;
  - khoảng hơn 90 ngày;
  - tài khoản không có trong danh sách.
- **Truy vấn** (migration `021_SecurityAuditFilter.sql`; không sửa 020):
  - `usp_SecurityAuditList` nhận thêm `@FromUtc` (gồm), `@ToUtcExclusive` (không gồm), `@UserId` và `@UnknownAccounts`; trả về các dòng mới nhất và tổng số dòng khớp.
  - Ngày Việt Nam được ứng dụng đổi sang UTC: 00:00 ngày đầu và 00:00 ngày sau ngày cuối.
  - Thêm chỉ mục theo `UserId, OccurredAt`.
  - Thủ tục mới `usp_SecurityAuditAccounts` trả danh sách tài khoản.
  - Cả hai thủ tục vẫn kiểm tra quyền `Audit.Read`.
- Các phần từ Task 1 giữ nguyên: màn hình chỉ dành cho Quản lý, chỉ xem, mới nhất lên đầu.

## Chạy demo

```powershell
cd D:\HeThongDatBan-Ordering-Chinh
$env:RM_CONNECTION_STRING = 'Server=.\MSSQLSERVER07;Database=RestaurantManagement_Dev;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True'
dotnet run --project tools/RestaurantManagement.DbTool -- migrate      # phải thấy "Applied: 021_SecurityAuditFilter.sql"
```

1. Chạy web, đăng nhập bằng tài khoản Quản lý và mở **Nhật ký hệ thống**: ô ngày đã điền sẵn 7 ngày gần nhất, dòng tóm tắt ghi “Mặc định 7 ngày gần nhất”.
2. Đổi **Từ ngày / Đến ngày** sang khoảng khác và bấm **Lọc**: danh sách cập nhật.
3. Chọn một tài khoản cụ thể và bấm **Lọc**: chỉ còn sự kiện của tài khoản đó. Có thể kết hợp với khoảng ngày.
4. Chọn một khoảng ngày chưa có dữ liệu: danh sách rỗng hợp lệ.
5. Bấm **7 ngày gần nhất** để về mặc định.

## Kiểm thử

```powershell
dotnet build RestaurantManagement.sln --no-restore -m:1
dotnet run --project tests/RestaurantManagement.AreaTests --no-build
dotnet run --no-build --project tools/RestaurantManagement.DbTool -- verify
```

`SecurityAuditFilterVerification` (trong `verify`, SQL Server, đăng nhập Quản lý qua HTTP) chèn dữ liệu mẫu có thời điểm cố định, trong đó có các mốc biên `00:00:00.000` và `23:59:59.999` theo giờ Việt Nam. Các trường hợp kiểm tra:

- **Mặc định**: mở màn hình hiển thị đúng 7 ngày gần nhất, gồm cả 00:00 của ngày thứ 7 về trước và 23:59:59 hôm nay; loại ngày liền trước và ngày mai; mọi dòng nằm trong khoảng; ô ngày điền sẵn; tài khoản là “Tất cả”.
- **Lọc theo khoảng ngày** 05–10/03/2020: trả đúng 5 sự kiện, loại 23:59:59.999 ngày 04/03 và 00:00 ngày 11/03, vẫn mới nhất lên đầu.
- **Lọc theo tài khoản**: `waiter` chỉ trả sự kiện của waiter; “Định danh không tồn tại” chỉ trả sự kiện không có tài khoản; tài khoản đã chọn được giữ trên form.
- **Kết hợp** khoảng ngày và tài khoản `manager`: trả đúng 3 sự kiện.
- **Khoảng ngày không có dữ liệu**: danh sách rỗng hợp lệ, tổng 0, không báo lỗi.
- **Dữ liệu nhập sai**: ngày đảo ngược; 91 ngày bị từ chối, 90 ngày được chấp nhận; tài khoản ngoài danh sách; ngày sai định dạng. Database cũng từ chối khoảng thời gian đảo ngược.

`SecurityAuditTests` (không cần database) kiểm tra quy tắc mốc ngày Việt Nam/UTC, mặc định 7 ngày, ngày còn thiếu, giới hạn 90 ngày, giá trị tài khoản và tên tham số truy vấn.

## Chưa có trong task này

Phân trang; xuất dữ liệu. (Rà soát chỉ đọc và chặn truy cập với mọi vai trò: đã làm ở [Task 3](S1-05-Task3.md).)
