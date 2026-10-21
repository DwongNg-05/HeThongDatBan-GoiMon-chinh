# Triển khai Internet miễn phí — Azure App Service F1 + Azure SQL Free

Tài liệu này triển khai đầy đủ ứng dụng ASP.NET và SQL Server cho buổi nghiệm
thu S1–S4. **Không** sao chép `appsettings.Local.json`, Gmail App Password hoặc
chuỗi kết nối vào Git.

## Vì sao không dùng Render Free cho bản nghiệm thu này?

Ứng dụng hiện dùng SQL Server và gửi Gmail SMTP. Render Free chỉ cung cấp
PostgreSQL, database miễn phí hết hạn sau 30 ngày, đồng thời chặn SMTP cổng 587.
Vì vậy có thể đặt web lên Render, nhưng sẽ không đủ các luồng email/SQL của S1–S4.

Azure App Service F1 kết hợp Azure SQL Database Free phù hợp hơn cho demo: Azure
SQL tương thích `Microsoft.Data.SqlClient`, và SMTP xác thực cổng 587 được Azure
cho phép. Đây là môi trường demo/free, không phải production SLA.

## 1. Tạo Azure SQL Database Free

1. Vào Azure Portal → **Create a resource** → **Azure SQL** → **SQL database**.
2. Tạo resource group (ví dụ `rg-bepnha-demo`), SQL server mới và database
   `BepNhaDemo`.
3. Chọn **Configure database** → **Free offer / Serverless**. Bật lựa chọn
   auto-pause khi đạt giới hạn miễn phí để tránh phát sinh chi phí.
4. Trong Networking, tạm bật **Allow Azure services and resources to access this
   server**. Chỉ thêm IP máy cá nhân khi cần chạy migration từ máy.
5. Lưu connection string ADO.NET. Thay `User ID`/`Password` bằng thông tin SQL
   server vừa tạo và giữ `Encrypt=True;TrustServerCertificate=False`.

## 2. Tạo Web App miễn phí

1. **Create a resource** → **Web App**.
2. Publish: **Docker Container**; Operating system: **Linux**; pricing plan:
   **F1 Free**. Chọn region gần Azure SQL.
3. Sau khi tạo, vào **Deployment Center** và liên kết repo GitHub. Chọn branch
   cần nghiệm thu; Dockerfile nằm tại thư mục gốc repo.
4. Trong **Configuration → Application settings**, thêm các biến dưới đây.
   Thay `https://TEN-APP.azurewebsites.net` bằng URL thật của Web App:

| Key | Giá trị |
|---|---|
| `RM_CONNECTION_STRING` | Chuỗi ADO.NET của Azure SQL Database |
| `Booking__PublicBaseUrl` | `https://TEN-APP.azurewebsites.net` |
| `TableQr__PublicBaseUrl` | `https://TEN-APP.azurewebsites.net` |
| `DataProtection__KeyDirectory` | `/home/data-protection-keys` |
| `WEBSITES_ENABLE_APP_SERVICE_STORAGE` | `true` |
| `Email__Host` | `smtp.gmail.com` |
| `Email__Port` | `587` |
| `Email__EnableSsl` | `true` |
| `Email__UserName` | Gmail gửi thư |
| `Email__Password` | Gmail App Password — đặt dạng secret |
| `Email__FromAddress` | Gmail gửi thư |
| `Email__FromName` | `Bếp Nhà` |
| `Smtp__Host` | `smtp.gmail.com` |
| `Smtp__Port` | `587` |
| `Smtp__UseSsl` | `false` |
| `Smtp__Username` | Gmail gửi thư |
| `Smtp__Password` | Gmail App Password — đặt dạng secret |
| `Smtp__FromEmail` | Gmail gửi thư |
| `Smtp__FromName` | `Bếp Nhà` |

`Email__*` phục vụ xác minh đăng nhập và email đặt bàn. `Smtp__*` phục vụ worker
gửi hàng đợi email. Gmail yêu cầu **App Password**, không dùng mật khẩu đăng nhập
thường.

## 3. Áp dụng schema và nạp dữ liệu demo

Chạy các lệnh sau từ máy phát triển, thay chuỗi kết nối bằng Azure SQL của bạn.
Không đưa chuỗi kết nối vào source code hoặc lịch sử terminal chia sẻ công khai.

```powershell
$env:RM_CONNECTION_STRING = 'Server=tcp:TEN-SERVER.database.windows.net,1433;Initial Catalog=BepNhaDemo;User ID=...;Password=...;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'
$env:RM_DEMO_PASSWORD = 'Matkhaudemo1'

dotnet run --project tools/RestaurantManagement.DbTool -- migrate
dotnet run --project tools/RestaurantManagement.DbTool -- seed-demo
dotnet run --project tools/RestaurantManagement.DbTool -- seed-s4-demo
dotnet run --project tools/RestaurantManagement.DbTool -- seed-s4-live-demo
```

Sau đó kiểm tra URL Internet `/Menu` và đăng nhập bằng bốn tài khoản `manager`,
`waiter`, `kitchen`, `cashier`. Mật khẩu là giá trị của `RM_DEMO_PASSWORD` ở trên.

## 4. Kiểm tra trước buổi nghiệm thu

1. Mở `https://TEN-APP.azurewebsites.net/Menu` từ 4G hoặc thiết bị khác mạng.
2. Đăng nhập bốn vai trò, thử một luồng từ [kịch bản S4-10](S4-10-Task3-acceptance.md).
3. Tạo một đặt bàn bằng email thật; xác nhận email và kiểm tra link mở đúng
   `azurewebsites.net`, không phải `localhost`.
4. Chỉ để dữ liệu demo trên môi trường này. F1 và Azure SQL Free có giới hạn
   quota, có thể bị pause khi không dùng; mở lại URL thì dịch vụ tự hoạt động lại.

## Lưu ý vận hành

- Azure SQL Free có quota hàng tháng. Bật auto-pause khi hết quota, theo dõi Cost
  Management để không phát sinh phí ngoài ý muốn.
- F1 phù hợp demo/đồ án, không phù hợp website sản xuất hoặc nhiều người dùng.
- Thư mục `/home/data-protection-keys` giúp liên kết xác nhận/hủy đặt bàn còn hợp
  lệ sau khi Web App khởi động lại. Ảnh món tải lên trong lúc demo nên được giữ
  trong Azure Storage nếu cần tồn tại qua nhiều lần triển khai.
