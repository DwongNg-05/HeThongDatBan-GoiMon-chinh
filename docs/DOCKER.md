# Triển khai bằng Docker

Bộ triển khai gồm .NET 10, SQL Server 2022 Express, công cụ migration và Caddy cấp HTTPS. Dùng máy Linux x86-64 hoặc Docker Desktop ở chế độ Linux containers; nên có ít nhất 4 GB RAM. Cần Docker Engine và Docker Compose v2. Máy này chưa bật Docker Engine nên chưa kiểm thử chạy container thực tế.

## 1. Cấu hình

Chạy tại thư mục gốc dự án. Trên PowerShell:

```powershell
Copy-Item .env.example .env
```

Trên Linux:

```sh
cp .env.example .env
chmod 600 .env
```

Sửa `.env`:

- `MSSQL_SA_PASSWORD`: mật khẩu riêng, tối thiểu 12 ký tự có chữ hoa, thường, số và ký hiệu. Đặt trong dấu nháy đơn, không dùng dấu nháy kép trong mật khẩu SQL vì chuỗi kết nối dùng ký tự này để bao mật khẩu. Không commit `.env`.
- `SITE_HOST`: tên miền thật, ví dụ `bepnha.example.com`, không kèm giao thức hoặc đường dẫn. Trỏ DNS A về IP máy chủ; nếu có AAAA thì IPv6 cũng phải tới máy chủ. Mở TCP 80 và 443 cho Caddy cấp chứng chỉ. Các cổng này phải còn trống.
- `SMTP_HOST`, `SMTP_PORT`, `SMTP_USERNAME`, `SMTP_PASSWORD`, `SMTP_FROM`: thông tin email của bạn. Mặc định cổng 587 với STARTTLS. Nếu dùng Gmail, dùng mật khẩu ứng dụng.
- `DB_NAME`: mặc định `RestaurantManagement_Dev`; database mới hoàn toàn, không tự sao chép dữ liệu SQL Server trên máy Windows của bạn.

SQL Server không được mở cổng ra Internet. Mặc định Express; lựa chọn edition khác phải phù hợp giấy phép. Cấu hình ban đầu dùng `sa` để dễ khởi tạo. Trước khi vận hành thực tế, tạo SQL login riêng với các quyền ứng dụng cần rồi đặt `RM_WEB_CONNECTION_STRING`; giữ tài khoản quản trị cho migration. Không thay mật khẩu SA trong `.env` rồi kỳ vọng mật khẩu của database đã tồn tại tự đổi: cần đổi trong SQL Server trước.

## 2. Khởi động

**Điều kiện build Release:** phiên bản `SixLabors.ImageSharp 4.1.2` đang dùng yêu cầu giấy phép SixLabors hợp lệ. Đặt file giấy phép tại `.local/sixlabors.lic` hoặc chỉ định đường dẫn trong `SIXLABORS_LICENSE_FILE` của `.env` (nếu có khoảng trắng, bao bằng nháy đơn). File được truyền bằng BuildKit secret, không chép vào image hay Git. Khi chưa có giấy phép, build sẽ dừng; cần lấy giấy phép phù hợp từ nhà cung cấp trước. Lần kiểm tra trên máy hiện tại đã xác nhận Release bị chặn vì thiếu giấy phép.

```sh
docker compose config --quiet
docker compose up -d --build
docker compose ps -a
docker compose logs --tail=100 migrate web proxy
```

SQL Server phải healthy, `migrate` kết thúc với mã 0, sau đó web mới chạy. Migration dùng công cụ sẵn có của dự án và bỏ qua các file đã áp dụng. Nếu migration thất bại, xem log và sửa lỗi trước khi khởi động lại; web sẽ không chạy trên database thiếu migration.

Mở `https://TEN-MIEN-CUA-BAN`. Nếu giữ `SITE_HOST=localhost`, mở `https://localhost`: Caddy sử dụng chứng chỉ CA nội bộ, máy của bạn chưa tin CA nên trình duyệt sẽ báo chứng chỉ chưa được tin cậy. Có thể lấy chứng chỉ CA bằng `docker compose cp proxy:/data/caddy/pki/authorities/local/root.crt ./caddy-local-root.crt` rồi nhập vào kho chứng chỉ tin cậy của máy dùng thử. Khi dùng tên miền công khai, Caddy xin chứng chỉ công khai tự động.

Chưa cấu hình SMTP thì email của phần `Email` được lưu trong `/app/App_Data/emails`, không gửi tới khách. Có thể xem bằng `docker compose exec web sh -c 'ls /app/App_Data/emails'`. Cần SMTP hoạt động để dùng xác minh email trên môi trường thật.

## 3. Dữ liệu và tài khoản dùng thử (tùy chọn)

Database mới chưa có tài khoản nhân viên hay thực đơn. Chỉ nạp dữ liệu mẫu trên môi trường demo, không chạy trên database sản xuất. Trên PowerShell, nhập mật khẩu rồi chạy:

```powershell
$demoSecret = Read-Host 'Mật khẩu tài khoản demo' -AsSecureString
$env:RM_DEMO_PASSWORD = [System.Net.NetworkCredential]::new('', $demoSecret).Password
try {
    docker compose run --rm -e RM_DEMO_PASSWORD migrate seed-demo
} finally {
    Remove-Item Env:RM_DEMO_PASSWORD
}
```

Trên Linux Bash:

```bash
read -r -s -p 'Mật khẩu tài khoản demo: ' RM_DEMO_PASSWORD
export RM_DEMO_PASSWORD
docker compose run --rm -e RM_DEMO_PASSWORD migrate seed-demo
unset RM_DEMO_PASSWORD
```

Tạo các tài khoản `manager`, `waiter`, `kitchen`, `cashier` dùng mật khẩu vừa nhập. Mật khẩu ít nhất 8 ký tự có chữ và số. Với demo chưa có email, có thể đặt `EMAIL_VERIFICATION_ENABLED=false` rồi chạy `docker compose up -d web`; khi triển khai thật giữ `true` và cấu hình SMTP cùng email tài khoản.

## 4. Cập nhật phiên bản

Sao lưu database trước khi cập nhật. Sau khi lấy mã mới:

```sh
docker compose stop web
docker compose build web migrate
docker compose run --rm migrate migrate
# Chỉ thực hiện khi migration ở trên thành công.
docker compose up -d --force-recreate migrate web proxy
```

Migration không tự hoàn tác khi quay về image cũ. Nếu bước migration báo lỗi, giữ web dừng và kiểm tra log.

## 5. Dữ liệu bền vững

- `sql-data`: toàn bộ SQL Server.
- `app-data`: khóa mã hóa cookie/liên kết xác nhận và email lưu tạm.
- `dish-images`: ảnh món tải lên.
- `caddy-data`, `caddy-config`: chứng chỉ và trạng thái Caddy.

`docker compose down` giữ lại các volume. **Không dùng `docker compose down -v` nếu cần giữ dữ liệu**, vì lệnh này xóa volume. Volume không thay thế bản sao lưu: sao lưu SQL bằng công cụ SQL Server, sao lưu ảnh và App_Data ra nơi khác và kiểm tra phục hồi định kỳ. Dữ liệu/thư mục upload có sẵn trên máy không tự nhập vào volume mới.

Bộ này chạy một bản web vì phiên đăng nhập và sự kiện sơ đồ bàn đang dùng bộ nhớ của một tiến trình. Nếu subnet `172.28.231.0/24` trùng mạng máy chủ, đổi subnet, địa chỉ proxy và `ReverseProxy__KnownProxy` trong `compose.yaml` đồng thời.

Tham khảo: [Microsoft: container .NET và người dùng app](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-8/containers), [Microsoft: SQL Server Linux containers](https://learn.microsoft.com/sql/linux/sql-server-linux-docker-container-deployment?view=sql-server-ver16).
