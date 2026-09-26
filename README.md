# Hệ thống đặt bàn và gọi món nhà hàng

Database SQL Server và bộ khung ASP.NET Core MVC .NET 10 cho nhóm phát triển.

## Trạng thái hiện tại

- Đã có 8 migration SQL, bao gồm quản lý khu vực và tên khu vực lưu trên đặt bàn cũ.
- Đã kiểm thử bằng SQL Server: 50 yêu cầu đặt cùng bàn đồng thời, 10 lần gửi thanh toán đồng thời, giá món tại thời điểm gọi, quyền chuyển trạng thái bếp, giảm giá, chốt ca, gộp bàn và thu hồi phiên QR.
- Database trên máy người tạo: `RestaurantManagement_Dev`, server `.\MSSQLSERVER07`.
- Đã có màn hình quản lý khu vực (thêm, sửa, ngừng sử dụng, xóa có kiểm tra liên kết), tạo/danh sách/chi tiết đặt bàn. Luồng này dùng stored procedure/SqlClient; chưa có đăng nhập web. Tài khoản mẫu vẫn là dữ liệu cho chức năng đăng nhập sau này.

GitHub lưu **mã nguồn tạo database**, không lưu database đang chạy hay dữ liệu thật. Mỗi thành viên chạy các bước dưới đây để tạo database riêng.

## Yêu cầu

- .NET SDK 10.
- SQL Server 2022 trở lên trên Windows; có thể dùng SQL Server Developer hoặc Express.
- Tài khoản Windows có quyền tạo database trên instance dùng cho phát triển.
- SSMS để xem bảng và chạy truy vấn nếu cần.

## Lấy mã và tạo database

Mở PowerShell:

```powershell
git clone https://github.com/DwongNg-05/HeThongDatBan-GoiMon-chinh.git
cd HeThongDatBan-GoiMon-chinh
dotnet restore RestaurantManagement.sln --locked-mode

# Đổi tên server phù hợp với máy của bạn, ví dụ .\SQLEXPRESS.
$env:RM_CONNECTION_STRING = 'Server=.\MSSQLSERVER07;Database=RestaurantManagement_Dev;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True'
dotnet run --project tools/RestaurantManagement.DbTool -- migrate
dotnet run --project tools/RestaurantManagement.DbTool -- check
```

Lệnh `migrate` tạo database nếu chưa có và áp dụng từng migration trong giao dịch. Chạy lại sẽ bỏ qua các migration đã áp dụng. Sau đó mở SSMS, kết nối instance của bạn và Refresh mục Databases.

`TrustServerCertificate=True` dành cho môi trường phát triển cục bộ. Khi triển khai thật, cấu hình chứng chỉ hợp lệ và tài khoản ứng dụng có quyền tối thiểu; không dùng tài khoản quản trị để chạy website.

## Dữ liệu mẫu (tuỳ chọn)

Database vừa tạo có 4 vai trò, 19 quyền và lịch mở cửa. Chưa có tài khoản nhân viên, bàn hay món ăn.

Để nạp dữ liệu mẫu vào database mới:

```powershell
$demoPassword = Read-Host 'Mật khẩu mẫu: ít nhất 8 ký tự, có chữ và số' -AsSecureString
$env:RM_DEMO_PASSWORD = [System.Net.NetworkCredential]::new('', $demoPassword).Password
try {
    dotnet run --project tools/RestaurantManagement.DbTool -- seed-demo
} finally {
    Remove-Item Env:RM_DEMO_PASSWORD -ErrorAction SilentlyContinue
}
```

Tạo 4 tài khoản `manager`, `waiter`, `kitchen`, `cashier`, mật khẩu được băm bcrypt; 3 khu vực (Tầng một, Tầng hai, Sân vườn), mỗi khu vực 20 bàn, 5 nhóm món, 60 món và 20 đặt bàn trong 7 ngày. Mã bàn theo khu vực A01–A20, B01–B20, C01–C20. Không ghi mật khẩu vào Git. Lệnh nạp lại không ghi đè dữ liệu mẫu đã có. Không chạy seed trên database sản xuất.

Trạng thái bàn dùng các giá trị `Available`, `Reserved`, `Serving`, `Cleaning`; sơ đồ luôn kèm nhãn chữ tương ứng. Nghiệp vụ hiện có chuyển bàn sang `Reserved` khi xác nhận đặt bàn gần giờ đến, `Serving` khi mở phiên phục vụ, `Cleaning` khi thanh toán đóng phiên, rồi `Available` hoặc `Reserved` khi nhân viên hoàn tất dọn bàn. Tác vụ bảo trì đồng bộ bàn trống/đã đặt theo đặt bàn còn hiệu lực. Sơ đồ demo phát sự kiện Server-Sent Events tới các trình duyệt mở cùng máy chủ; khi kết nối lại, trình duyệt tải snapshot mới nhất. Bộ dữ liệu demo nằm trong bộ nhớ của một tiến trình, chưa phát trạng thái từ các thủ tục SQL hoặc chia sẻ giữa nhiều tiến trình web.

## Kiểm thử

```powershell
dotnet build RestaurantManagement.sln --no-restore
dotnet run --project tools/RestaurantManagement.DbTool -- verify
```

`verify` tạo database `RestaurantManagement_Test_<mã ngẫu nhiên>` trên server đang cấu hình, chạy các kiểm thử tích hợp rồi xoá **database kiểm thử đó**. Không thay đổi dữ liệu `RestaurantManagement_Dev`. Tài khoản chạy cần quyền tạo và xoá database kiểm thử.

## Cấu trúc

| Thư mục | Nội dung |
| --- | --- |
| `database/migrations` | Tạo bảng, ràng buộc, thủ tục, view, vai trò và dữ liệu cấu hình |
| `database/seeds` | Dữ liệu mẫu phát triển |
| `tools/RestaurantManagement.DbTool` | Tạo/cập nhật database, nạp mẫu, kiểm thử |
| `src/RestaurantManagement.Data` | Thư viện dữ liệu, đã tham chiếu EF Core SQL Server |
| `src/RestaurantManagement.Web` | Bộ khung MVC .NET 10 |

## Quy ước database

- Thời điểm lưu theo UTC; ngày nghiệp vụ và báo cáo quy đổi UTC+7.
- Tiền VND dùng `decimal(18,0)`; tỷ lệ giảm giá dùng `decimal(18,2)`.
- Đơn giá, tên món và đơn vị được chụp lại trên order và hoá đơn.
- Các thao tác nghiệp vụ chính đi qua stored procedure có giao dịch. Không dùng thao tác sửa bảng trực tiếp để thay thế quy trình thanh toán, đặt bàn hoặc chuyển trạng thái món.
- `ActorUserId` phải lấy từ người dùng đã xác thực ở phía máy chủ, không tin giá trị do trình duyệt gửi. Stored procedure kiểm tra quyền nhưng không thay thế xác thực web.
- Chưa triển khai worker gửi SMTP, cập nhật SignalR, lịch chạy tác vụ, sao lưu, xuất PDF hoặc toàn bộ giao diện. Cấu trúc dữ liệu đã chuẩn bị cho các phần này.
- `usp_RunMaintenance` xử lý đặt lại món tạm hết, xếp email nhắc lịch vào hàng đợi và xoá dữ liệu đặt bàn quá 12 tháng. Cần cấu hình lịch chạy khi phát triển worker.

## Làm việc nhóm trên GitHub

1. Mỗi người dùng database trên máy riêng; chia sẻ migration qua Git, không chia sẻ file `.mdf`, `.ldf` hay `.bak`.
2. Tạo nhánh theo công việc: `git switch -c feature/ten-chuc-nang`.
3. **Không sửa migration đã áp dụng.** Thêm file mới, ví dụ `006_AddFeature.sql`, rồi chạy `migrate` và `verify`. Công cụ dùng checksum để phát hiện migration cũ bị thay đổi.
4. Push nhánh và tạo Pull Request để thành viên khác review trước khi hợp nhất.
5. Sau khi lấy thay đổi mới bằng `git pull`, chạy lại `dotnet restore` và `migrate`.

Không commit mật khẩu, chuỗi kết nối có thông tin đăng nhập, dữ liệu khách thật hoặc thư mục build. `.gitignore` đã loại các tệp cấu hình cục bộ, database vật lý và thư mục build thông dụng.

## S1-06: quản lý khu vực

Xem [báo cáo Lát 2–4](docs/S1-06-Task2-4-review.md) để biết quy tắc tên, nâng cấp migration 008, kiểm thử HTTP và kịch bản demo. Migration dừng nếu các tên cũ trùng sau chuẩn hóa, không tự gộp hoặc xóa dữ liệu.
