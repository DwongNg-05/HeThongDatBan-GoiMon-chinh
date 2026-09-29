# Hệ thống đặt bàn và gọi món nhà hàng

Database SQL Server và bộ khung ASP.NET Core MVC .NET 10 cho nhóm phát triển.

## Trạng thái hiện tại

- Đã có 8 migration SQL: 41 bảng (gồm bảng theo dõi migration), 35 stored procedure, 7 view.
- Đã kiểm thử bằng SQL Server: 50 yêu cầu đặt cùng bàn đồng thời, 10 lần gửi thanh toán đồng thời, giá món tại thời điểm gọi, quyền chuyển trạng thái bếp, giảm giá, chốt ca, gộp bàn và thu hồi phiên QR.
- Database trên máy người tạo: `RestaurantManagement_Dev`, server `.\MSSQLSERVER07`.
- Đã hoàn tất S1-01: đăng nhập bằng tên tài khoản hoặc số điện thoại, phiên đăng nhập có định danh, màn hình quản lý thực đơn và truy vết người đổi giá/trạng thái món.

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

Tạo 4 tài khoản `manager`, `waiter`, `kitchen`, `cashier`, mật khẩu được băm bcrypt; 3 khu vực, 25 bàn, 5 nhóm món, 60 món và 20 đặt bàn trong 7 ngày. Không ghi mật khẩu vào Git. Lệnh nạp lại không ghi đè dữ liệu mẫu đã có. Không chạy seed trên database sản xuất.

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
| `src/RestaurantManagement.Web` | MVC .NET 10: đăng nhập và quản lý thực đơn |

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
3. **Không sửa migration đã áp dụng.** Thêm file mới, ví dụ `009_AddFeature.sql`, rồi chạy `migrate` và `verify`. Công cụ dùng checksum để phát hiện migration cũ bị thay đổi.
4. Push nhánh và tạo Pull Request để thành viên khác review trước khi hợp nhất.
5. Sau khi lấy thay đổi mới bằng `git pull`, chạy lại `dotnet restore` và `migrate`.

Không commit mật khẩu, chuỗi kết nối có thông tin đăng nhập, dữ liệu khách thật hoặc thư mục build. `.gitignore` đã loại các tệp cấu hình cục bộ, database vật lý và thư mục build thông dụng.


## Demo S1-01 — đăng nhập và truy vết

### Database đã có tài khoản (ví dụ `tester`)

Lệnh `seed-login-demo` bổ sung một quản lý demo và 3 món ăn, không yêu cầu database trống. Chạy lại không đổi mật khẩu, giá hoặc trạng thái món đã có. Không dùng trên database sản xuất.

```powershell
$env:RM_CONNECTION_STRING = 'Server=.\MSSQLSERVER07;Database=RestaurantManagement_Dev;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True'
$env:RM_DEMO_USERNAME = 'demo-manager'
$env:RM_DEMO_PHONE = '0000000099' # Số giả lập chỉ dùng demo
$demoPassword = Read-Host 'Mật khẩu tài khoản demo' -AsSecureString
$env:RM_DEMO_PASSWORD = [System.Net.NetworkCredential]::new('', $demoPassword).Password
try {
    dotnet run --project tools/RestaurantManagement.DbTool -- seed-login-demo
} finally {
    Remove-Item Env:RM_DEMO_PASSWORD -ErrorAction SilentlyContinue
    Remove-Item Env:RM_DEMO_USERNAME -ErrorAction SilentlyContinue
    Remove-Item Env:RM_DEMO_PHONE -ErrorAction SilentlyContinue
}
```

Để dùng tài khoản đã có, đặt `RM_DEMO_USERNAME` và `RM_DEMO_PHONE` đúng theo tài khoản đó. Tài khoản phải là quản lý đang hoạt động và đã có mật khẩu băm. Mật khẩu hiện tại được giữ nguyên dù nhập mật khẩu khác khi nạp lại. Nếu định danh xung đột, lệnh dừng và hoàn tác toàn bộ.

Demo: đăng nhập bằng tên → đổi giá một món → kiểm tra tên người thực hiện trong “Thay đổi gần đây” → đăng xuất → đăng nhập bằng số điện thoại → đổi trạng thái món → kiểm tra lịch sử. Nhập sai mật khẩu hoặc định danh không tồn tại đều nhận cùng một thông báo chung.

Chỉ chạy một phiên website tại một thời điểm: nếu dùng F5 trong Visual Studio, hãy dừng phiên `dotnet run` trước để tránh chiếm cổng 7114/5105.

### Database mới

Sau khi cấu hình `RM_CONNECTION_STRING`, chạy `migrate` và `seed-demo` theo hướng dẫn trên, khởi động:

```powershell
dotnet run --project src/RestaurantManagement.Web --launch-profile https
```

Mở địa chỉ HTTPS được in trong cửa sổ chạy. Đăng nhập bằng `manager` hoặc `0900000001`, với mật khẩu đã nhập lúc nạp dữ liệu mẫu. Nếu đã seed từ trước, mật khẩu cũ được giữ nguyên. Tài khoản phải đang hoạt động.

Màn hình quản lý hiển thị tên tài khoản, cho phép đổi giá và đánh dấu món tạm hết/mở bán lại. Bảng “Thay đổi gần đây” hiển thị người thực hiện và thời điểm UTC+7. Người thực hiện lấy từ mã tài khoản trong cookie xác thực được bảo vệ ở máy chủ, không lấy từ dữ liệu biểu mẫu. Giá được ghi vào `MenuPriceHistory` và `AuditLogs`; trạng thái món được ghi vào `MenuAvailabilityEvents`.

Phiên dùng cookie HttpOnly, không ghi mật khẩu vào cookie, không lưu cookie lâu dài trên trình duyệt; đăng xuất thu hồi phiên trên máy chủ và xoá cookie. Vé xác thực có hạn tối đa 14 ngày, không gia hạn theo thao tác. Đã có khóa đăng nhập theo Task 2 và hết phiên sau 30 phút không thao tác theo Task 3 bên dưới.

`dotnet run --no-build --project tools/RestaurantManagement.DbTool -- verify` sau khi build Debug sẽ tự chạy ứng dụng web trên cổng cục bộ và database kiểm thử riêng. Bộ kiểm thử bao gồm đăng nhập bằng cả hai định danh, lỗi chung, tài khoản vô hiệu hoá, hash bcrypt, phiên đúng tài khoản, đăng xuất, chống gửi biểu mẫu thiếu token và bỏ qua mã người thực hiện giả khi đổi thực đơn/giá; sau đó chạy các kiểm thử nghiệp vụ database hiện có.

## S1-01 Task 2 — khóa đăng nhập tạm thời

Sau khi lấy mã mới, dừng website, build và chạy `migrate` trước khi khởi động lại để áp dụng `007_LoginLockout.sql`. Không sửa hoặc chạy lại nội dung migration cũ bằng tay.

- Mỗi lần xác thực thất bại được lưu thời điểm UTC trong `LoginFailures`. Cửa sổ tính lỗi là `(hiện tại - 15 phút, hiện tại]`; lỗi ở đúng mốc 15 phút hoặc cũ hơn không được cộng.
- Sai lần thứ 5 trong cửa sổ này khóa đăng nhập 15 phút kể từ lần thứ 5. Tên đăng nhập và số điện thoại chia sẻ cùng trạng thái tài khoản.
- `LoginLockoutSubjects` lưu hạn khóa; các cột `Users.FailedLoginCount`, `FailureWindowStartedAt`, `LockedUntil` phản ánh trạng thái tại lần xử lý gần nhất. Dữ liệu cũ được dọn khi xử lý lần đăng nhập tiếp theo.
- Trong thời gian khóa, mật khẩu đúng cũng bị từ chối. Yêu cầu bị chặn vẫn được ghi thời điểm nhưng không tăng bộ đếm và không kéo dài khóa.
- Hết khóa, lần thử tiếp theo bắt đầu cửa sổ mới. Đăng nhập thành công xóa các lần thất bại liên quan, bộ đếm và hạn khóa, đồng thời ghi `LastLoginAt`.
- Giao diện luôn có lỗi chung; sau ngưỡng thất bại còn hiển thị thời gian chờ dạng `mm:ss`, giảm theo thời gian thực. Kiểm tra phía máy chủ vẫn quyết định cho phép đăng nhập; tải lại trang hoặc sửa đồng hồ phía trình duyệt không bỏ qua được khóa.
- Định danh không tồn tại cũng dùng thông báo và ngưỡng chờ giống nhau, không tạo tài khoản. Định danh này chỉ được lưu dưới dạng SHA-256 trong khóa theo dõi. Không trả thông báo “tài khoản không tồn tại”.
- Các quyết định được tuần tự hóa bằng giao dịch và khóa bản ghi để nhiều yêu cầu đồng thời không vượt ngưỡng. Mật khẩu vẫn được kiểm tra bằng bcrypt và không lưu trong nhật ký thất bại.

Demo bằng một tài khoản mẫu đã đăng xuất: nhập sai 4 lần để thấy vẫn thử lại được; lần thứ 5 xuất hiện `15:00`; nhập đúng vẫn bị từ chối trong thời gian khóa; đợi hết thời gian rồi nhập đúng để vào quản lý. Không thay đổi thời gian máy hoặc dữ liệu khóa khi demo thực tế.

Kiểm thử:

```powershell
dotnet build RestaurantManagement.sln --no-restore -m:1
dotnet run --no-build --project tools/RestaurantManagement.DbTool -- verify
node tools/tests/login-lockout.test.cjs
```

Bộ kiểm thử bao gồm lần sai 1–5, dùng luân phiên tên/số điện thoại, mật khẩu đúng khi khóa, thời gian còn lại, khóa không bị kéo dài, hết hạn, lần sai ngoài cửa sổ 15 phút, xóa lỗi sau thành công, thông báo định danh không tồn tại và 10 yêu cầu sai đồng thời. Các mốc hết hạn được mô phỏng bằng thời điểm dữ liệu trong database kiểm thử riêng; ứng dụng thật không có chức năng bỏ qua khóa. Kiểm thử JavaScript xác nhận đếm ngược vẫn đúng khi tab bị tạm dừng và hiển thị lời nhắc thử lại lúc về 0.

## S1-01 Task 3 — hết phiên sau 30 phút không thao tác

Chạy `migrate` để áp dụng `008_IdleSessions.sql` trước khi khởi động bản mới. Cookie tạo từ bản cũ không có mã phiên máy chủ sẽ bị từ chối; người dùng đăng nhập lại một lần.

- Mỗi lần đăng nhập thành công tạo một bản ghi `LoginSessions` với mã ngẫu nhiên, `UserId`, `CreatedAt`, `LastActivityAt` và hạn tối đa `ExpiresAt`. Cookie được bảo vệ chứa mã phiên và định danh người dùng, không chứa mật khẩu.
- Máy chủ xác thực phiên trước khi cho phép thực hiện chức năng cần đăng nhập. Khi `LastActivityAt <= thời gian UTC hiện tại - 30 phút`, phiên bị thu hồi vĩnh viễn bằng `RevokedAt`; cookie cũ không thể mở lại phiên.
- Truy cập màn hình quản lý và đổi giá/trạng thái thành công làm mới thời gian hoạt động. Yêu cầu thiếu token chống giả mạo, dữ liệu không hợp lệ, lỗi nghiệp vụ, trang công khai và tải ảnh/CSS/JavaScript không gia hạn phiên. Chỉ di chuột/gõ phím mà không gửi thao tác hợp lệ tới máy chủ cũng không gia hạn.
- Lần truy cập chức năng cần đăng nhập sau khi hết phiên được chuyển về đăng nhập với thông báo: “Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.” Yêu cầu đổi dữ liệu bằng phiên đã hết hạn bị chặn trước khi chạy nghiệp vụ.
- Nếu trang công khai hoặc tài nguyên phát hiện hết phiên trước, thông báo được giữ trong TempData để vẫn hiển thị ở lần mở trang đăng nhập tiếp theo.
- Đăng nhập lại tạo mã phiên mới; đăng xuất chủ động thu hồi phiên hiện tại. Việc hết phiên được kiểm tra ở lần gửi yêu cầu tiếp theo, không tự đóng màn hình đang mở bằng bộ đếm phía trình duyệt.
- Thời gian được quyết định bởi SQL Server, không lấy từ biểu mẫu hoặc đồng hồ trình duyệt. Kiểm tra và cập nhật phiên dùng giao dịch; không làm sống lại phiên đã hết hạn hoặc đã thu hồi.

Demo: đăng nhập → mở quản lý hoặc cập nhật món → không gửi thao tác trong 30 phút → tải lại quản lý để thấy thông báo hết phiên → đăng nhập lại và sử dụng bình thường.

Lệnh `verify` bao gồm kiểm tra phiên ở phút 29, làm mới thời gian bằng truy cập và cập nhật hợp lệ, không gia hạn bằng tải tài nguyên/yêu cầu lỗi, hết hạn từ phút 30, thông báo trên trang đăng nhập, chặn POST đổi giá sau hết hạn, phát lại cookie cũ, đăng nhập lại tạo phiên mới và thu hồi phiên khi đăng xuất. Mốc 30 phút được mô phỏng bằng dữ liệu thời gian trong database kiểm thử riêng; không có đường tắt thay đổi hạn phiên trên ứng dụng thật.
