# S2-07 Task 4 — Phiếu chốt điều kiện nghiệm thu với PO

**Trạng thái: CHỜ PO XÁC NHẬN.** Ngày lập: 02/10/2026. Các nội dung đề xuất dưới đây chưa phải quyết định của PO.

## 1. Máy quầy chuẩn

| Nội dung cần chốt | Đề xuất / thông tin đã biết | Giá trị PO xác nhận |
|---|---|---|
| Máy hoặc mã tài sản dùng nghiệm thu | Chưa xác định máy quầy thực tế | Chưa xác nhận |
| CPU | Máy đã đo: Intel Core i7-8750H 2,20 GHz, 6 nhân/12 luồng | Chưa xác nhận |
| RAM | Máy đã đo: 15,9 GiB | Chưa xác nhận |
| Hệ điều hành | Máy đã đo: Windows 11 Pro 10.0.26300 | Chưa xác nhận |
| Trình duyệt và phiên bản | Đã đo Chrome headless 154.0.8037.92; đề nghị nghiệm thu thêm Chrome có giao diện trên máy quầy | Chưa xác nhận |
| Màn hình, độ phân giải và mức zoom | Đã đo viewport 1366 × 768 ở kịch bản đơn; đề nghị ghi cấu hình màn hình thực tế, zoom 100% | Chưa xác nhận |
| Vị trí web server và SQL Server | Đã đo cùng máy; cần ghi cấu hình và vị trí triển khai thực tế | Chưa xác nhận |
| Tải nền trong khi nghiệm thu | Đề nghị dùng các ứng dụng quầy thường chạy và ghi số máy khách đồng thời | Chưa xác nhận |

Không coi máy phát triển là máy quầy chuẩn nếu PO chưa đồng ý.

## 2. Đường truyền chuẩn

| Nội dung cần chốt | Giá trị PO xác nhận |
|---|---|
| LAN hay Wi-Fi; tuyến máy quầy → web server → SQL Server | Chưa xác nhận |
| Băng thông tải xuống/tải lên | Chưa xác nhận |
| Độ trễ và mất gói tới web server | Chưa xác nhận |
| Số máy sử dụng đồng thời | Chưa xác nhận |
| Điều kiện mạng chậm phải kiểm tra | Chưa xác nhận |

Kết quả hiện có dùng HTTP loopback và SQL shared memory. Hồ sơ mạng chậm 4 Mbps tải xuống, 1 Mbps tải lên, độ trễ 100 ms là **mô phỏng kiểm thử**, chưa phải mạng chuẩn do PO chốt. Khi nghiệm thu cần ghi số đo mạng thực tế và ngày đo.

## 3. Mốc bắt đầu và “hiển thị xong”

**Đề xuất:** bắt đầu từ lúc điều hướng mở trang sơ đồ (`navigationStart`), sau khi đã đăng nhập. Kết thúc khi đủ 60 ô bàn có bố cục, mã bàn/sức chứa/nhãn trạng thái xuất hiện, chức năng bấm mở chi tiết và tự cập nhật đã sẵn sàng. Tiêu chí là **thấy đủ các ô trong sơ đồ và thao tác được**, gồm các ô bên dưới cần cuộn; không yêu cầu 60 ô cùng nằm trong một khung màn hình.

Mốc kết thúc được công cụ hiện tại ghi sau hai lượt `requestAnimationFrame`, khi đủ ô có kích thước và các bộ xử lý đã gắn. Không chờ tải nội dung bảng chi tiết vì dữ liệu đó chỉ lấy sau khi bấm bàn. Phép đo mở bảng chi tiết được ghi riêng. Đây là tín hiệu sẵn sàng của trình duyệt; cần kiểm tra trực quan trên màn hình quầy khi nghiệm thu.

PO có thể chọn mốc chỉ thấy đủ 60 ô hoặc mốc thao tác được. Nếu chọn mốc khác đề xuất, phải cập nhật công cụ đo và đo lại, không đổi tên kết quả cũ thành kết quả theo mốc mới.

**Mốc PO xác nhận:** Chưa xác nhận.

## 4. Quy tắc nghiệm thu đề xuất

- Dùng đúng máy và mạng được xác nhận ở trên, với dữ liệu 60 bàn đủ bốn trạng thái, đặt bàn và món đã gọi.
- Giữ tự cập nhật và bảng chi tiết hoạt động. Ghi 10 lượt liên tiếp, gồm lượt mở đầu tiên; không loại bỏ mẫu chậm. Mỗi lượt phải **nhỏ hơn 2.000 ms**.
- Ghi riêng mở lại, mạng chậm, nhiều máy cùng cập nhật, dữ liệu 80 bàn và thao tác sau thời gian chạy kéo dài. PO xác nhận thời lượng chạy kéo dài và số máy đồng thời.
- Lưu từng kết quả, cấu hình máy, điều kiện mạng, thời điểm đo và người nghiệm thu. Kết quả tại máy phát triển hiện có chỉ là bằng chứng thử nghiệm trước nghiệm thu.

## 5. Xác nhận

| Nội dung | Giá trị |
|---|---|
| PO / người có quyền quyết định | Chưa cung cấp |
| Ngày xác nhận | Chưa xác nhận |
| Các mục đã được duyệt | Chưa xác nhận |
| Bằng chứng xác nhận (biên bản, ticket hoặc trao đổi) | Chưa cung cấp |
| Người thực hiện nghiệm thu tại quầy | Chưa cung cấp |
| Kết quả AC5 tại quầy | Chưa nghiệm thu |

Chỉ đánh dấu “đã chốt với PO” khi có xác nhận thực tế cho cả máy, mạng và mốc hiển thị. Chỉ đánh dấu AC5 đạt sau khi đo trên các điều kiện đã chốt.

Tham chiếu: [báo cáo đo và hướng dẫn chạy lại](S2-07-Task4-performance.md).
