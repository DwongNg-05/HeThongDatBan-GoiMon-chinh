# S2-07 Task 4 — Hiệu năng sơ đồ bàn

Đã triển khai tối ưu và đo bằng Chrome thật, ứng dụng ASP.NET thật và SQL Server thật trên máy hiện tại ngày 02/10/2026 (giờ Việt Nam). Tất cả lượt mở được đo đều dưới 2.000 ms. Đây là kết quả kiểm thử tại máy phát triển; chưa phải biên bản nghiệm thu AC5 trên cấu hình máy quầy do PO phê duyệt.

## Điều kiện đo và quyết định cần PO xác nhận

Phiếu ghi nhận máy quầy chuẩn, mạng chuẩn, mốc hiển thị và người phê duyệt: [S2-07-Task4-PO-confirmation.md](S2-07-Task4-PO-confirmation.md). Hiện trạng là **chờ PO xác nhận**; thông số máy đã đo không tự động trở thành tiêu chuẩn nghiệm thu.

- Máy hiện tại: Intel Core i7-8750H 2,20 GHz, 12 luồng; RAM 15,9 GiB; Windows 11 Pro 10.0.26300.
- Chrome headless 154.0.8037.92; web và SQL chạy cùng máy. HTTP loopback và SQL shared memory, chưa đo mạng LAN/Wi-Fi thực tế tại quầy.
- Tạm định nghĩa “hiển thị xong”: đủ 60 ô có kích thước bố cục, các bộ xử lý bấm mở chi tiết và tự cập nhật đã gắn, sau hai lượt requestAnimationFrame. Đồng hồ bắt đầu từ navigationStart, gồm tải HTML, tài nguyên, truy vấn và bố cục. Đây là tín hiệu sẵn sàng trong trình duyệt, không phải phép đo quang học thời điểm pixel xuất hiện trên màn hình vật lý.
- PO cần xác nhận cấu hình máy quầy, trình duyệt, đường truyền chuẩn và định nghĩa trên. Hồ sơ số liệu giữ `acceptanceStandardConfirmed: false` và `approvedByPO: false` cho đến khi có xác nhận.

## Thay đổi đã triển khai

- Đọc sơ đồ bằng SQL bất đồng bộ và cancellation token; lấy watermark và bàn trong một lượt giao tiếp SQL. Giữ khóa theo thứ tự cập nhật để không bỏ sót thay đổi đồng thời.
- Thêm migration `024_TableMapPerformanceIndexes.sql`: chỉ mục bao phủ bàn và khu vực đang hoạt động. Đã áp dụng vào RestaurantManagement_Dev.
- API sơ đồ chỉ gửi khu vực, mã bàn, sức chứa, trạng thái và mốc đồng bộ; thông tin khách, đặt bàn, phiên phục vụ và tiền chỉ lấy khi mở bảng chi tiết.
- Trang sơ đồ bỏ tải jQuery không dùng. Khi đồng bộ chỉ sửa ô đổi trạng thái, cập nhật số bàn trống bằng chênh lệch; bỏ thao tác ép trình duyệt tính lại bố cục để chạy hiệu ứng.
- Thêm tín hiệu `table-map-ready` và công cụ đo tự động, không đưa số liệu kỹ thuật vào giao diện nhân viên.
- Bộ dữ liệu riêng có ba khu vực, 60 bàn chia đủ bốn trạng thái; có khách, đặt bàn xác nhận, phiên phục vụ và ba món mỗi bàn đang phục vụ. Kịch bản 80 bàn thêm 20 bàn để đo dư địa.

## Kết quả đo

| Kịch bản | Số lượt | Thấp nhất | Trung bình | Cao nhất | Dưới 2 giây mọi lượt |
|---|---:|---:|---:|---:|---|
| 60 bàn, ngữ cảnh trình duyệt mới, cache trống | 10 | 152 ms | 200 ms | 344 ms | Đạt |
| 60 bàn, mở lại trong cùng ngữ cảnh | 10 | 87 ms | 98 ms | 123 ms | Đạt |
| 60 bàn, mạng chậm mô phỏng | 10 | 1.059 ms | 1.069 ms | 1.083 ms | Đạt |
| 60 bàn, 6 màn hình cùng tải, đồng thời ghi trạng thái | 60 | 290 ms | 764 ms | 1.708 ms | Đạt |
| 80 bàn, cache trống | 10 | 196 ms | 254 ms | 532 ms | Đạt |
| 80 bàn, mở lại | 10 | 56 ms | 77 ms | 107 ms | Đạt |

Mạng chậm được giả lập bằng Chrome DevTools Protocol: độ trễ 100 ms, tải xuống 4 Mbps và tải lên 1 Mbps. Sáu màn hình dùng ngữ cảnh trình duyệt riêng với cùng tài khoản thử; không thay thế sáu máy vật lý. Lượt mở đầu tiên được giữ trong chuỗi cache trống, không bỏ mẫu chậm.

Mười lượt cache trống 60 bàn: **344, 251, 184, 193, 189, 152, 163, 194, 163, 162 ms**.

Trong phiên chạy 120 giây, 24 lần bấm mở chi tiết mất 49–80 ms (trung bình 64 ms); không ghi nhận tác vụ JavaScript dài từ 50 ms trong khoảng theo dõi này. Bảng đang mở nhận thay đổi trạng thái sau 1.664 ms, tính cả thao tác ghi từ máy khách thứ hai. Có 165 mẫu lấy thay đổi hoàn tất, trung bình 65 ms, cao nhất 886 ms; phản hồi chỉ 76–125 byte. Không có lỗi JavaScript. Hai phút không tương đương một ca phục vụ dài nhiều giờ.

Số liệu gốc: [60 bàn](performance/S2-07-tables-60.json), [80 bàn](performance/S2-07-tables-80.json), [cấu hình](performance/S2-07-environment.json). [Ảnh bảng chi tiết đang chạy với 60 bàn](performance/S2-07-table-details-60.png).

## Chạy lại

Tại thư mục gốc dự án trong PowerShell, máy cần .NET 10, SQL Server, Chrome và Node có Playwright. Tài khoản Windows cần tạo/xóa cơ sở dữ liệu kiểm thử trên SQL Server.

```powershell
dotnet build RestaurantManagement.sln --no-restore -m:1 -p:UseAppHost=false -p:OutputPath="$PWD/.local/task4-verify/"
./tools/performance/Measure-TableMap.ps1 -TableCounts 60,80 -SoakSeconds 120
```

Tham số `-ConnectionString`, `-NodePath` và `-NodeModules` cho phép chọn SQL và runtime khác. Giá trị mặc định phù hợp máy phát triển hiện tại. Báo cáo nằm trong `.local/task4-measurements`. Công cụ tạo cơ sở dữ liệu `RestaurantManagement_Perf_<GUID>`, mật khẩu ngẫu nhiên chỉ dùng trong tiến trình, máy chủ riêng cổng 5217, rồi dừng tiến trình do nó tạo và xóa đúng cơ sở dữ liệu đó. Không nạp bộ dữ liệu thử vào dữ liệu nhà hàng đang sử dụng. Cổng 5217 cần trống.

Để nghiệm thu tại quầy, chạy ứng dụng và Chrome trên cấu hình PO đã chốt, đo đúng mạng chuẩn và giữ toàn bộ 10 lượt; có thể tăng `-SoakSeconds` lên 3.600 để kiểm tra một giờ. Bộ đo hiện tại tạo máy chủ cùng máy, nên phép đo web/SQL qua mạng thực tế cần một phiên đo riêng trên cấu hình triển khai được PO chọn.

## Kiểm tra chức năng và trạng thái nghiệm thu

Build thành công, không cảnh báo/lỗi; 230 kiểm tra .NET, 13 kiểm tra JavaScript và kiểm tra SQL tích hợp đều đạt. Các kiểm tra bao gồm nhóm khu vực, bốn trạng thái, quyền xem, cập nhật đồng thời, bảng chi tiết, tạm tính, lỗi dữ liệu, mất mạng/khôi phục, tab ẩn và giữ vị trí cuộn; kiểm tra mất mạng và cuộn dùng bộ kiểm thử JavaScript, không phải thử rút dây mạng tại quầy.

Phần triển khai Task 4 và phép đo trên máy hiện tại đã hoàn thành. **Chưa ký đạt AC5 hay toàn bộ AC1–AC5**: còn xác nhận của PO, đo lại trên máy quầy/mạng chuẩn và kiểm tra độ mượt trong thời gian vận hành thực tế.
