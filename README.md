# Hệ thống đặt bàn và gọi món nhà hàng

Database SQL Server và bộ khung ASP.NET Core MVC .NET 10 cho nhóm phát triển.

## Trạng thái hiện tại

- Đã có 12 migration SQL, gồm sự kiện outbox ghi lại mọi lần trạng thái bàn thay đổi.
- Đã kiểm thử bằng SQL Server: 50 yêu cầu đặt cùng bàn đồng thời, 10 lần gửi thanh toán đồng thời, giá món tại thời điểm gọi, quyền chuyển trạng thái bếp, giảm giá, chốt ca, gộp bàn và thu hồi phiên QR.
- Database trên máy người tạo: `RestaurantManagement_Dev`, server `.\MSSQLSERVER07`.
- Đã có màn hình quản lý khu vực (thêm, sửa, ngừng sử dụng, xóa có kiểm tra liên kết), tạo/danh sách/chi tiết đặt bàn. Luồng này dùng stored procedure/SqlClient; đã gộp chức năng tài khoản từ EP-01.
- Trigger SQL ghi trạng thái cũ/mới sau mỗi cập nhật đã commit; worker web đọc outbox mỗi 500 ms và phát SSE tới các sơ đồ đang mở.

GitHub lưu **mã nguồn tạo database**, không lưu database đang chạy hay dữ liệu thật. Mỗi thành viên chạy các bước dưới đây để tạo database riêng.

## Yêu cầu

- .NET SDK 10.
- SQL Server 2022 trở lên trên Windows; có thể dùng SQL Server Developer hoặc Express.
- Tài khoản Windows có quyền tạo database trên instance dùng cho phát triển.
- SSMS để xem bảng và chạy truy vấn nếu cần.

## Lấy mã và tạo database

Mở PowerShell:

```powershell
git clone https://github.com/DwongNg-05/HeThongDatBan-Ordering-chinh.git
cd HeThongDatBan-Ordering-chinh
dotnet restore RestaurantManagement.sln --locked-mode

# Đổi tên server phù hợp với máy của bạn, ví dụ .\SQLEXPRESS.
$env:RM_CONNECTION_STRING = 'Server=.\MSSQLSERVER07;Database=RestaurantManagement_Dev;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True'
dotnet run --project tools/RestaurantManagement.DbTool -- migrate
dotnet run --project tools/RestaurantManagement.DbTool -- check
```

Lệnh `migrate` tạo database nếu chưa có và áp dụng từng migration trong giao dịch. Chạy lại sẽ bỏ qua các migration đã áp dụng. Sau đó mở SSMS, kết nối instance của bạn và Refresh mục Databases.

`TrustServerCertificate=True` dành cho môi trường phát triển cục bộ. Khi triển khai thật, cấu hình chứng chỉ hợp lệ và tài khoản ứng dụng có quyền tối thiểu; không dùng tài khoản quản trị để chạy website.

### Kết nối ứng dụng web

Ứng dụng web và worker thông báo trạng thái bàn cùng đọc chuỗi kết nối `RM_CONNECTION_STRING`. Nếu biến này không có, ứng dụng dùng `ConnectionStrings:DefaultConnection` trong `appsettings.json` (mặc định là `RestaurantManagement_Dev` trên `.\MSSQLSERVER07`). Có thể ghi đè trong PowerShell trước khi chạy web:

```powershell
$env:RM_CONNECTION_STRING = 'Server=.\MSSQLSERVER07;Database=RestaurantManagement_Dev;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True'
dotnet run --project src/RestaurantManagement.Web
```

Hãy chạy `migrate` trước khi khởi động worker để tạo bảng outbox và trigger trạng thái bàn. Chuỗi kết nối chỉ tồn tại trong cửa sổ PowerShell hiện tại; không lưu thông tin đăng nhập vào Git.

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

Trạng thái bàn dùng các giá trị `Available`, `Reserved`, `Serving`, `Cleaning`; sơ đồ luôn kèm nhãn chữ tương ứng. Mỗi thay đổi thực phát một sự kiện gồm trạng thái cũ/mới, mã bàn, thời điểm UTC và lý do chuyển trạng thái: xác nhận đặt (`Available → Reserved`), bắt đầu phục vụ (`Available/Reserved → Serving`), đóng phiên (`Serving → Cleaning`), hoàn tất dọn (`Cleaning → Available/Reserved`) hoặc huỷ/hết hạn đặt (`Reserved → Available`). Gửi lại cùng trạng thái không phát sự kiện. Sơ đồ demo phát sự kiện Server-Sent Events tới các trình duyệt mở cùng máy chủ; khi kết nối lại, trình duyệt tải snapshot mới nhất. Bộ dữ liệu demo nằm trong bộ nhớ của một tiến trình, chưa phát trạng thái từ các thủ tục SQL hoặc chia sẻ giữa nhiều tiến trình web.

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

## S1-04 — phân quyền theo vai trò

Chạy `migrate` để áp dụng `031_RolePermissionsByScope.sql` và `032_RemoveKitchenDishes.sql`.

| Vai trò | Thấy và dùng được |
| --- | --- |
| Phục vụ | Đặt bàn, Sơ đồ bàn, Gọi món, Món chờ mang ra |
| Bếp | Màn hình bếp (báo món tạm hết làm ở Quản lý món, chỉ Quản lý) |
| Thu ngân | Thanh toán, Hoá đơn, Chốt ca |
| Quản lý | Toàn bộ |

Máy chủ kiểm tra quyền ở cả 101 API và trong thủ tục SQL. Khi bị chặn:
- gõ thẳng đường dẫn ngoài quyền: nhận **403** và trang “Không có quyền truy cập”, có nút về màn hình chính;
- gọi API: nhận 401/403 dạng JSON.

Kiểm thử bằng `dotnet run --no-build --project tools/RestaurantManagement.DbTool -- verify-api-permissions`. Tổng hợp: [docs/S1-04-TongHop.md](docs/S1-04-TongHop.md); chi tiết từng lát: [Task 1](docs/S1-04-Task1.md), [Task 2](docs/S1-04-Task2.md), [Task 3](docs/S1-04-Task3.md), [Task 4](docs/S1-04-Task4.md).

## S1-06: quản lý khu vực

Xem [báo cáo Lát 2–4](docs/S1-06-Task2-4-review.md) để biết quy tắc tên, nâng cấp migration 008, kiểm thử HTTP và kịch bản demo. Migration dừng nếu các tên cũ trùng sau chuẩn hóa, không tự gộp hoặc xóa dữ liệu.

## S1-07: quản lý bàn và mã QR

Chạy `migrate` để áp dụng `013_TableQrManagement.sql` trước khi khởi động website. Migration chỉ bổ sung token QR công khai và thủ tục xoay QR; không tạo dữ liệu bàn mới.

Mở **Quản lý bàn** để thêm/sửa bàn, xem chi tiết QR, tải PNG hoặc tải PDF QR theo khu vực. Bàn cũ chưa có QR có nút **Tạo mã QR**; xuất PDF chỉ tự tạo QR cho các bàn trong khu vực vừa chọn.

Từ S1-04 Task 4, thao tác tạo hoặc xoay QR ghi dưới tài khoản đang đăng nhập (chỉ Quản lý); `AreaManagement:ActorUserId` trong cấu hình không còn được dùng.

Kiểm thử thủ công: chọn một bàn → **Mở thử QR** phải hiển thị đúng mã bàn/khu vực/sức chứa; tải PDF khu vực phải chứa QR kèm mã bàn; **Sinh lại QR** rồi mở URL cũ phải thấy `Mã QR đã thay đổi. Vui lòng gọi phục vụ.` Nếu quét bằng điện thoại, đặt `TableQr__PublicBaseUrl` thành địa chỉ HTTPS mà điện thoại truy cập được; không commit địa chỉ IP hoặc mật khẩu máy cá nhân.

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

## S2-01 Task 1 — thực đơn công khai

Khách mở `/Menu` trên điện thoại mà không cần đăng nhập, xem món theo nhóm với ảnh, tên, mô tả ngắn và giá VND (`45.000 ₫`). API công khai: `GET /api/menu`. Nạp dữ liệu mẫu bằng `dotnet run --project tools/RestaurantManagement.DbTool -- seed-menu-demo` (chạy lại an toàn, không cần mật khẩu). Muốn thực đơn phong phú hơn, chạy thêm `seed-menu-extra` để bổ sung 50 món ăn, đồ uống thuộc 8 nhóm (Khai vị, Món chính, Lẩu, Món nướng, Hải sản, Món chay, Tráng miệng, Đồ uống); món đã có cùng tên được giữ nguyên. Quy tắc món đang bán / hết trong ngày / ngừng bán, kịch bản demo và kiểm thử: xem [docs/S2-01-Task1.md](docs/S2-01-Task1.md).

## S1-05 Task 1 — nhật ký đăng nhập và sửa giá món

Chạy `migrate` để áp dụng `020_SecurityAuditLog.sql`. Đăng nhập thành công, đăng nhập thất bại và sửa giá món được ghi vào kho riêng `dbo.SecurityAuditLogs` (chỉ thêm, không sửa/xoá) kèm thời điểm, tài khoản, vai trò, IP. Quản lý xem tại **Nhật ký hệ thống** (`/AuditLogs`), mới nhất lên đầu; vai trò khác bị từ chối. Chi tiết, demo và kiểm thử: [docs/S1-05-Task1.md](docs/S1-05-Task1.md).

## S1-05 Task 2 — lọc nhật ký

Chạy `migrate` để áp dụng `021_SecurityAuditFilter.sql`. Màn hình **Nhật ký hệ thống** có bộ lọc khoảng ngày (giờ Việt Nam, tối đa 90 ngày) và tài khoản (chọn từ danh sách); mặc định 7 ngày gần nhất. Quy tắc cần chốt với PO, demo và kiểm thử: [docs/S1-05-Task2.md](docs/S1-05-Task2.md).

## S1-05 Task 3 — nhật ký chỉ đọc, kiểm soát truy cập

Chạy `migrate` để áp dụng `022_AuditReadOnlyHardening.sql`. Nhật ký bảo mật, nhật ký nghiệp vụ và lịch sử giá chỉ được thêm: trigger chặn sửa/xoá, bảng chặn khoá ngoại ngăn `TRUNCATE`, tài khoản ứng dụng `restaurant_app` bị `DENY` thao tác trực tiếp, EF từ chối sửa/xoá. Màn hình **Nhật ký hệ thống** và **Lịch sử giá** chỉ dành cho Quản lý, kể cả khi gõ đường dẫn trực tiếp; quyền bị thu hồi khi đang đăng nhập cũng bị chặn. Kết quả rà soát, demo và kiểm thử: [docs/S1-05-Task3.md](docs/S1-05-Task3.md).

## S3-01 Task 1 — quét QR bàn trống, vào phiên gọi món mới

Chạy `migrate` để áp dụng `043_S301QrGuestSession.sql`. Khách quét QR của bàn **Trống** (cần có ca đang mở) sẽ vào thẳng trang **Gọi món tại bàn** (`/TableOrder`) của đúng bàn, không cần đăng nhập. Hệ thống tạo đúng một phiên phục vụ mới, đồng thời chuyển bàn sang **Đang phục vụ** trong cùng giao dịch. Mở đường dẫn QR (GET) không thay đổi dữ liệu. Quét lặp hoặc nhiều người quét cùng lúc không tạo thêm phiên. Quy tắc “bàn trống” cần PO chốt, demo và kiểm thử (`verify-qr-session`): [docs/S3-01-Task1.md](docs/S3-01-Task1.md).

## S3-01 Task 2 — quét QR bàn đang phục vụ, vào chung phiên

Chạy `migrate` để áp dụng `045_S301JoinOpenTableSession.sql`. Khách quét QR của bàn đang có phiên mở sẽ vào **chung phiên đó**, không tạo phiên mới, và thấy đầy đủ các món bàn đã gọi trước đó, mỗi lượt có ghi rõ người gọi: Bạn gọi / Khách cùng bàn gọi / Nhân viên gọi. Món đang chọn (chưa gửi bếp) được tách riêng khỏi món đã gọi. Bàn đang chờ thanh toán thì không nhận thêm khách. Chi tiết: [docs/S3-01-Task2.md](docs/S3-01-Task2.md).

## S3-01 Task 3 — chặn QR đã sinh lại và bàn đang dọn

Chạy `migrate` để áp dụng `046_S301QrVersionAndCleaning.sql`. Mỗi mã QR có phiên bản (`TableQrCodes.Version`). Quét mã cũ đã bị sinh lại thì nhận thông báo “Mã QR đã thay đổi”; quét QR của bàn đang dọn thì nhận “Bàn đang được dọn”. Cả hai đều kèm hướng dẫn gọi phục vụ (mã bàn, nút gọi nhà hàng) và không tạo phiên. Chi tiết: [docs/S3-01-Task3.md](docs/S3-01-Task3.md).

## S3-01 Task 4 — thanh toán đóng phiên, quét lại QR mở phiên mới

Không cần migration mới. Thanh toán (`usp_Checkout`) sẽ đóng phiên, trả bàn (chuyển Đang dọn), thu hồi phiên khách và giữ nguyên mã QR. Điện thoại khách tải lại trang sẽ thấy “Bàn … đã thanh toán” mà không còn món cũ. Sau khi dọn xong, quét lại cùng QR sẽ mở đúng một phiên mới không có món của phiên trước. Quy tắc cần PO chốt, demo và kiểm thử: [docs/S3-01-Task4.md](docs/S3-01-Task4.md).

## S3-03 Task 1 — ghi chú riêng cho từng món

Không cần migration mới. Trên trang **Gọi món tại bàn** (`/TableOrder`), mỗi dòng món trong giỏ có ô ghi chú tự do tối đa 200 ký tự, kèm bộ đếm. Ô không nhận ký tự thứ 201, và máy chủ từ chối ghi chú quá dài chứ không tự cắt. Bấm “+ Thêm vào giỏ” với món đã có ghi chú, hoặc bấm “+ Dòng ghi chú khác”, sẽ tạo dòng riêng. Hai dòng cùng món nhưng khác ghi chú được lưu thành hai dòng, không gộp số lượng. Bấm **Đặt món** sẽ mở bước xác nhận liệt kê món kèm ghi chú, khách bấm **Xác nhận gửi bếp** mới gửi. Quy tắc cần PO chốt, demo và kiểm thử (`node --test tools/tests/table-order-notes.test.cjs`, `verify-qr-session`): [docs/S3-03-Task1.md](docs/S3-03-Task1.md).

## S3-03 Task 2 — lựa chọn ghi chú nhanh

Chạy `migrate` để áp dụng `047_S303QuickNotes.sql`. Mỗi dòng món trong giỏ có thêm 3 nút **Ít cay**, **Không hành**, **Không mỡ**, chọn được nhiều nút và bấm lại để bỏ chọn. Các lựa chọn này đi kèm ghi chú tự do (200 ký tự) của chính dòng đó. Bước xác nhận hiện đủ lựa chọn và ghi chú của từng dòng. Lựa chọn được lưu ở cột riêng `OrderItems.QuickNotes`, nên không chiếm chỗ của ghi chú tự do. Quy tắc cần PO chốt, demo và kiểm thử: [docs/S3-03-Task2.md](docs/S3-03-Task2.md).

## S3-03 Task 3 — ghi chú rõ ràng trên màn hình bếp

Không cần migration mới (dùng cột `QuickNotes` của Task 2). Trên **Màn hình bếp**, mỗi dòng món hiển thị lựa chọn nhanh (nhãn đỏ) và ghi chú tự do ngay dưới tên món, trong khung nền vàng. Chữ cỡ 16–18px, tự ngắt dòng và không bao giờ bị cắt, kể cả ghi chú đủ 200 ký tự. Hai dòng cùng món nhưng khác yêu cầu vẫn là hai dòng riêng. Chi tiết: [docs/S3-03-Task3.md](docs/S3-03-Task3.md).

## S3-04 Task 4 — giữ ghi chú trên hoá đơn tạm tính

Chạy `migrate` để áp dụng `048_S304KeepOrderNotes.sql`. Phục vụ mở **Sơ đồ bàn**, bấm bàn đang có khách rồi bấm **Xem hoá đơn tạm tính**; Thu ngân mở từ trang **Thanh toán**. Hoá đơn hiển thị mỗi dòng đơn là một dòng riêng, nên các dòng cùng món không bị gộp. Ngay dưới tên món là lựa chọn nhanh và ghi chú, đọc từ đúng hai cột mà màn hình bếp đọc. Trigger trong database chặn sửa ghi chú đã gửi bếp, nên khi đơn được cập nhật hay hoá đơn được mở lại, nội dung vẫn giữ nguyên. Chi tiết: [docs/S3-04-Task4.md](docs/S3-04-Task4.md).

## S3-05 Task 1 — huỷ món đang chờ bếp

Chạy `migrate` để áp dụng `056_S305CancelOrderItem.sql`. Trên **Hoá đơn tạm tính**, Phục vụ bấm **Hủy món** ở dòng còn chờ bếp. Hộp xác nhận bắt buộc chọn một lý do (khách đổi ý, gọi nhầm, hết nguyên liệu) và không chọn sẵn. Huỷ xong, dòng không còn tính tiền, tạm tính giảm đúng số tiền của dòng, và món rời màn hình bếp ở lần tải lại kế tiếp (mỗi 3 giây). Món bếp đã bắt đầu thì nút của Phục vụ bị vô hiệu kèm lý do. Nếu bếp bắt đầu nấu trong lúc nhân viên đang chọn lý do, máy chủ từ chối và hoá đơn hiện trạng thái mới. Mỗi lần huỷ được ghi nhật ký (dòng món, người huỷ, thời điểm, lý do); bấm lặp hay nhiều người cùng bấm chỉ huỷ và trừ tiền một lần. Quy tắc cần PO chốt, demo và kiểm thử (`verify-cancel-item`): [docs/S3-05-Task1.md](docs/S3-05-Task1.md).

## S3-05 Task 2 — báo cáo huỷ món cuối ca

Chạy `migrate` để áp dụng `057_S305ShiftCancellationReport.sql`. Quản lý mở **Báo cáo huỷ món** (`/Reports/Shift/Cancellations`, hoặc liên kết trên màn hình **Chốt ca**) và chọn ca. Báo cáo liệt kê mọi lần huỷ món của ca: huỷ thành công và yêu cầu bị từ chối. Mỗi mục có đơn (hoá đơn, bàn, đợt), dòng món, người thực hiện, thời điểm, lý do và cách tính tiền (không tính tiền, vẫn tính tiền, hoặc không đổi). Ca không có lần huỷ nào hiện "Ca này không có lần huỷ món nào."; lỗi database hiện thông báo không tải được báo cáo. Chỉ Quản lý xem được, cả trang lẫn API `/api/reports/shift-cancellations`. Quy tắc cần PO chốt, demo và kiểm thử (`verify-cancel-report`): [docs/S3-05-Task2.md](docs/S3-05-Task2.md).

## S3-05 Task 3 — Quản lý huỷ món đã bắt đầu chế biến (vẫn tính tiền)

Chạy `migrate` để áp dụng `058_S305ChargedCancellation.sql`. Trên **Hoá đơn tạm tính**, Quản lý bấm **Hủy có tính tiền** ở món đang nấu hoặc đã xong (món đã mang ra không huỷ được). Hộp xác nhận bắt buộc chọn một trong ba lý do và đánh dấu ô xác nhận huỷ có tính tiền. Dòng món giữ trên hoá đơn với nhãn “Đã huỷ có tính tiền”; tạm tính và hoá đơn không giảm. Màn hình bếp gỡ món và hiện thẻ “DỪNG MÓN” trong vòng 5 giây, cho tới khi Bếp bấm **Đã dừng món**. Lần huỷ vào nhật ký và báo cáo cuối ca. Quy tắc cần PO chốt, demo và kiểm thử (`verify-charged-cancel`): [docs/S3-05-Task3.md](docs/S3-05-Task3.md).

## S4-01 Task 1 — xem hoá đơn tạm tính của bàn đang phục vụ

Không cần migration mới. Trên **Sơ đồ bàn**, bấm vào bàn đang phục vụ rồi bấm **Tạm tính**. Màn hình hiện bảng món (số lượng, đơn giá, thành tiền, ghi chú), nhóm riêng **“Đã huỷ – có tính tiền”** cho món huỷ sau khi bếp đã nấu, và dòng **Tổng cộng** dạng `1.250.000 đ`. Món huỷ trước khi bếp nấu không xuất hiện; bàn chưa gọi món thì hiện “Bàn chưa có món nào.”. Nút **← Quay lại gọi món** mở lại chi tiết bàn. Xem tạm tính chỉ đọc: bàn vẫn đang phục vụ và gọi thêm món bình thường. Dữ liệu cũng có qua API `GET /api/tables/{mã bàn}/provisional-bill` (Quản lý, Phục vụ). Chưa có phần đầu hoá đơn, chưa in, chưa xuất PDF. Quy tắc cần PO chốt (món huỷ có tính tiền, làm tròn, ký hiệu tiền), demo và kiểm thử (`verify-bill-preview`): [docs/S4-01-Task1.md](docs/S4-01-Task1.md).

## S4-01 Task 2 — phần đầu hoá đơn tạm tính

Không cần migration mới. Màn hình **Tạm tính** có thêm phần đầu nằm trên bảng món, gồm: dòng **TẠM TÍNH – KHÔNG PHẢI HOÁ ĐƠN THANH TOÁN**, tên quán (lấy từ cấu hình chung `dbo.RestaurantSettings.Name`), mã bàn hiện tại (bàn đã chuyển hoặc gộp vẫn đúng), thời điểm bắt đầu phục vụ (lúc mở bàn), thời điểm in và tên nhân viên đang đăng nhập. Ngày giờ có dạng `15/10/2026 19:05`. Mỗi lần mở lại, thời điểm in được cập nhật; nhân viên khác mở thì tên người in đổi theo. API tạm tính có thêm `header`. Chưa in ra giấy, chưa xuất PDF. Quy tắc cần PO chốt (định nghĩa bắt đầu phục vụ, nguồn tên quán, dòng chữ tạm tính), demo và kiểm thử (`verify-bill-header`): [docs/S4-01-Task2.md](docs/S4-01-Task2.md).

## S4-01 Task 3 — in hoá đơn tạm tính khổ 80mm

Không cần migration mới. Trên màn hình **Tạm tính**, bấm **In**: hộp thoại in của trình duyệt mở ra với bản in khổ 80mm, dài theo nội dung, không có nút bấm hay thanh điều hướng. Bản in gồm phần đầu, bảng món (cột SL và Thành tiền cố định; tên món và ghi chú dài tự xuống dòng; ghi chú in dòng riêng dưới tên món), nhóm **ĐÃ HUỶ – CÓ TÍNH TIỀN** và **TỔNG CỘNG**. Nhiều món vẫn in liền một dải, không ngắt trang. In chỉ diễn ra ở trình duyệt, nên in hay huỷ đều không đổi trạng thái bàn, và vẫn gọi thêm món bình thường. Chưa xuất PDF. Mẫu bố cục cần PO chốt, cách cài đặt máy in, demo và kiểm thử (`verify-bill-print`, `tools/tests/bill-print*.cjs`): [docs/S4-01-Task3.md](docs/S4-01-Task3.md).

## S4-01 Task 4 — xuất hoá đơn tạm tính thành tệp PDF 80mm

Không cần migration mới. Trên màn hình **Tạm tính**, bấm **Xuất PDF**: trình duyệt tải về tệp `TamTinh_{mã bàn}_{yyyyMMdd-HHmm}.pdf` (theo giờ in). Tệp có khổ 80mm, cùng bố cục với bản in, một trang dài theo nội dung, gồm phần đầu, bảng món, nhóm món huỷ có tính tiền và tổng cộng. Chữ trong tệp là chữ thật, tiếng Việt có dấu chuẩn, nhờ font DejaVu Sans Condensed nhúng kèm (thư mục `src/RestaurantManagement.Web/Fonts`, giấy phép tự do). PDF được tạo ngay trên máy chủ, không dùng thư viện ngoài. Khi không tạo được tệp, màn hình hiện thông báo lỗi. Xuất PDF không đổi trạng thái bàn. Quy tắc đặt tên tệp cần PO chốt, demo và kiểm thử (`verify-bill-pdf`, `tools/tests/bill-pdf.test.cjs`): [docs/S4-01-Task4.md](docs/S4-01-Task4.md).

## S3-09 Task 1 — cảnh báo đặt bàn trễ 15 phút, đánh dấu khách không tới

Chạy `migrate` để áp dụng `049_S309LateReservationNoShow.sql`. Từ đúng mốc giờ hẹn + 15 phút, lượt **Chờ xác nhận** hoặc **Đã xác nhận** mà khách chưa tới sẽ tự hiện cảnh báo đỏ trên **Sơ đồ bàn** và **Danh sách đặt bàn**, không cần tải lại trang. Phục vụ hoặc Quản lý bấm **Khách không tới**: một giao dịch đổi lượt sang "Khách không tới", giải phóng bàn (bàn hiện Trống ngay) và ghi thêm một lần vào lịch sử số điện thoại (`dbo.ReservationNoShowHistory`). Bấm lặp hoặc nhiều người cùng bấm chỉ được ghi một lần. Khách đã được đánh dấu tới thì không bị giải phóng bàn nhầm. Quy tắc cần PO chốt, demo và kiểm thử (`verify-late-reservations`): [docs/S3-09-Task1.md](docs/S3-09-Task1.md).

## S3-09 Task 2 — gia hạn giữ bàn 15 phút (một lần)

Chạy `migrate` để áp dụng `054_S309ExtendReservationHold.sql`. Lượt đang có cảnh báo trễ mà chưa gia hạn có thêm nút **Gia hạn 15 phút** (Sơ đồ bàn và Danh sách đặt bàn). Gia hạn xong, cảnh báo được gỡ và hai màn hình hiện "Gia hạn giữ bàn tới HH:mm" (bàn vẫn Đã đặt trước). Hết hạn mà khách chưa tới thì cảnh báo tự hiện lại, không gia hạn lần hai được (kể cả bấm lặp hay nhiều người cùng bấm) nhưng vẫn **Khách không tới** được. Khách tới trong thời gian gia hạn thì không bị cảnh báo. Quy tắc thời hạn (đề xuất: lúc bấm + 15 phút, cần PO chốt), demo và kiểm thử (`verify-hold-extension`): [docs/S3-09-Task2.md](docs/S3-09-Task2.md).

## S3-09 Task 3 — cảnh báo số điện thoại nhiều lần không tới

Chạy `migrate` để áp dụng `055_S309PhoneNoShowWarning.sql`. Khi Quản lý hoặc Phục vụ tạo đặt bàn (form **Đặt bàn** khi đã đăng nhập, hoặc **Tạo lượt thủ công**), nhập hay đổi số điện thoại là hệ thống tra số lần khách không tới trong 90 ngày (theo số đã chuẩn hoá). Có ít nhất 3 lần thì hiện cảnh báo kèm số lần, và phải đánh dấu ô xác nhận mới tạo được. Máy chủ đếm lại khi gửi form, nên lịch sử vừa tăng trong lúc điền form vẫn bị phát hiện. Khách tự đặt không thấy lịch sử của số điện thoại và không bị chặn. Quy tắc cửa sổ 90 ngày và mốc biên (cần PO chốt), demo và kiểm thử (`verify-phone-no-show`): [docs/S3-09-Task3.md](docs/S3-09-Task3.md).

## S3-08 Task 1 — món vừa xong hiện ở danh sách chờ, xác nhận đã mang ra

Không cần migration mới. Khi Bếp bấm “Đã xong”, món hiện trên màn hình **Món chờ mang ra** (`/Serving`) của Phục vụ trong tối đa 5 giây, kèm thông báo “Món mới xong: Bàn A05 · Phở bò”. Danh sách có mã bàn, tên món, giờ xong, sắp từ sớm đến muộn. Bấm **Đã mang ra** thì món chuyển sang **Đã phục vụ** và rời danh sách ngay. Hai nhân viên cùng bấm một món thì chỉ một người được ghi nhận; người còn lại được báo ai đã mang ra. Quy tắc cần PO chốt, demo và kiểm thử: [docs/S3-08-Task1.md](docs/S3-08-Task1.md).

## S3-08 Task 2 — cảnh báo món chờ quá 5 phút

Không cần migration mới. Phục vụ (và Quản lý) mở **Món chờ mang ra** (`/Serving`) để thấy các món Bếp đã nấu xong nhưng chưa mang ra, sắp theo thời điểm hoàn thành (món xong trước ở trên). Đồng hồ “Đã chờ” tính từ lúc Bếp bấm “Đã xong” và chạy mỗi giây. Món chờ quá 5 phút được tô đỏ kèm nhãn “Chờ quá 5 phút” ngay trên danh sách, không đổi thứ tự. Bấm **Đã mang ra** thì món rời danh sách và không còn bị cảnh báo. Trang tự cập nhật mỗi 3 giây. Quy tắc cần PO chốt, demo và kiểm thử (`node --test tools/tests/serving-queue.test.cjs`, `verify-api-permissions`): [docs/Mon-cho-mang-ra.md](docs/Mon-cho-mang-ra.md).

## Đồng bộ tên bảng sang tiếng Anh

Chạy `migrate` để áp dụng `052_EnglishOrderingCartTableNames.sql`. Hai bảng giỏ món của màn hình Gọi món đổi tên: `dbo.DonHangs` → `dbo.Orders`, `dbo.DongHangs` → `dbo.OrderLines`; khoá chính, khoá ngoại, ràng buộc và chỉ mục cũng đổi theo (`PK_Orders`, `FK_OrderLines_Orders`, `CK_OrderLines_Quantity`, `IX_OrderLines_OrderId`). Dữ liệu, cột và quyền giữ nguyên. EF (`RestaurantDbContext`) ánh xạ `Order` → `Orders`, `OrderLine` → `OrderLines`. Sau bước này database không còn bảng nào mang tên tiếng Việt. Dừng website trước khi `migrate`, rồi khởi động lại.
