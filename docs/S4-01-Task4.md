# S4-01 Task 4: xuất hoá đơn tạm tính thành tệp PDF khổ 80mm

Trên màn hình **Tạm tính** có nút **Xuất PDF**. Bấm nút, trình duyệt tải về một tệp PDF đặt tên theo mã bàn và giờ in. Mở tệp ra là hoá đơn khổ 80mm, cùng bố cục với bản in (Task 3), đầy đủ phần đầu, bảng món, nhóm món huỷ có tính tiền và tổng cộng, với dấu tiếng Việt chuẩn. Bàn vẫn ở trạng thái đang phục vụ.

**Hoàn tất:** toàn bộ AC 3 (Task 3 in trực tiếp + Task 4 xuất PDF).

## Quy tắc đặt tên tệp (đề xuất, cần PO chốt)

**`TamTinh_{mã bàn}_{yyyyMMdd-HHmm}.pdf`**, theo **giờ in** (giờ Việt Nam). Ví dụ:

| Trường hợp | Tên tệp |
| --- | --- |
| Bàn A03, in lúc 22:30 ngày 15/10/2026 | `TamTinh_A03_20261015-2230.pdf` |
| Nhóm bàn đã gộp A03, A04 | `TamTinh_A03-A04_20261015-2230.pdf` |
| Mã bàn có dấu hoặc ký tự đặc biệt, ví dụ “Phòng VIP 1/2” | `TamTinh_Phong-VIP-1-2_….pdf` |

- Tên tệp chỉ dùng chữ không dấu, chữ số, `-` và `_`, nên an toàn khi gửi qua Zalo, email hay lưu trên Windows.
- Ngày viết theo dạng năm-tháng-ngày, nên các tệp tự xếp đúng thứ tự thời gian.
- Muốn đổi quy tắc, sửa `BillPdfFileName` trong `Models/Orders/BillPdfFileName.cs`.

## Cách hoạt động

- **`GET /ProvisionalBill/Preview/{mã bàn}/Pdf`** (Quản lý, Phục vụ):
  - Đọc dữ liệu tạm tính giống màn hình xem trước, chỉ đọc (`TableBillPreviewStore`).
  - Gắn phần đầu của lần xuất này (giờ in, người in).
  - Trả về tệp PDF tải về (`Content-Disposition: attachment`), tên theo quy tắc trên.

  Xuất PDF không đổi bàn, phiên hay đơn gọi món.
- **`BillPdfBuilder`** tạo PDF ngay trên máy chủ, **không dùng thư viện ngoài**, theo **đúng bố cục bản in 80mm**:
  - Trang rộng 80mm, lề 4mm. Cả hoá đơn là **một trang dài đúng bằng nội dung**, nên hoá đơn nhiều món vẫn liền một dải.
  - Thứ tự và các cột giống bản in: Món | SL 8mm | Thành tiền 22mm. Đơn giá, nhãn huỷ và ghi chú (chữ nghiêng) nằm dưới tên món.
  - Tên món dài, kể cả tên không có dấu cách, xuống dòng trong cột Món.
  - Cỡ chữ đổi từ px sang pt (12px = 9pt).
- **Chữ là chữ thật**, chọn, chép và tìm được.
  - Font nhúng là **DejaVu Sans Condensed** (thường, đậm, nghiêng), giấy phép tự do: `src/RestaurantManagement.Web/Fonts/LICENSE-DejaVu.txt`. Font này có đủ chữ tiếng Việt và có độ rộng gần với Arial của bản in.
  - Mỗi tệp chỉ nhúng những chữ có dùng, nên tệp chỉ khoảng 50KB.
  - Có bảng ToUnicode, nên tiếng Việt có dấu hiển thị đúng trên mọi máy, kể cả máy không cài font.
  - Font nằm trong thư mục `Fonts` của web và được chép kèm khi build hoặc publish (`.csproj`).
- **Báo lỗi:**
  - Nút **Xuất PDF** (`bill-pdf.js`) tải tệp qua `fetch`. Khi không tạo được tệp, thông báo hiện ngay trên màn hình tạm tính. Các trường hợp: bàn không còn khách, không đọc được dữ liệu, thiếu font, lỗi máy chủ, mất mạng, hết phiên đăng nhập.
  - Thông báo chung là: “Không tạo được tệp PDF. Vui lòng thử lại; nếu vẫn lỗi, bấm “In” rồi chọn máy in “Lưu dưới dạng PDF”.”
  - Nếu mở thẳng đường dẫn PDF, khi lỗi trang sẽ quay về màn hình tạm tính kèm thông báo.
  - Lỗi được ghi vào nhật ký máy chủ.

## Demo

1. Mở **Tạm tính** của một bàn đang phục vụ có vài món, một món ghi chú dài và một món huỷ có tính tiền. Bấm **Xuất PDF**.
2. Trình duyệt tải về một tệp, ví dụ `TamTinh_A03_20261015-2230.pdf`. Mở tệp ra thấy:
   - khổ 80mm;
   - dòng TẠM TÍNH, tên quán, bàn, giờ bắt đầu phục vụ, giờ in, người in;
   - bảng món;
   - nhóm **ĐÃ HUỶ – CÓ TÍNH TIỀN**;
   - **TỔNG CỘNG**;
   - dấu tiếng Việt chuẩn.
3. Quay lại bàn: bàn vẫn **Đang phục vụ** và gọi thêm món bình thường. Xuất PDF lần nữa, món mới có trong tệp.

## Kiểm thử

- **`BillPdfTests.cs`** (không cần database). Bộ này tạo PDF thật bằng font trong repo, rồi **đọc lại chữ trong tệp** (`PdfInspector`, giải mã qua ToUnicode như trình đọc PDF). Các điểm kiểm tra:
  - tệp PDF hợp lệ, một trang;
  - rộng đúng 80mm, dài theo nội dung;
  - 3 font được nhúng, mỗi font kèm bảng ToUnicode;
  - tiếng Việt có dấu đọc ra đúng, không thiếu chữ nào;
  - phần đầu khớp màn hình;
  - từng dòng món có tên, đơn giá, số lượng và thành tiền;
  - ghi chú dài in đủ;
  - món huỷ trước khi nấu không có trong tệp;
  - nhóm món huỷ có nhãn;
  - các dòng cộng và tổng cộng đúng;
  - tên dài xuống dòng trong cột Món;
  - hoá đơn 60 món vẫn một trang;
  - bàn chưa gọi món hiện thông báo trống;
  - tên tệp đúng quy tắc;
  - thiếu font thì báo lỗi;
  - có nút **Xuất PDF**;
  - quyền.
- **`node --test tools/tests/bill-pdf.test.cjs`** (đã chạy, 5/5 đạt):
  - tên tệp lấy từ máy chủ;
  - tải bằng đúng một yêu cầu GET;
  - lỗi từ máy chủ, trang không phải PDF và mất mạng đều hiện thông báo;
  - nút trở lại bình thường sau khi xong.
- **`verify-bill-pdf`** (SQL Server thật + web thật; chạy cùng bộ S4-01, cũng chạy trong `verify`):
  - tệp tải về là PDF một trang, khổ 80mm, tên đúng quy tắc theo giờ in;
  - **nội dung tệp khớp màn hình** (phần đầu, từng dòng món, ghi chú dài, nhóm huỷ, tổng);
  - xuất nhiều lần không đổi bàn, phiên hay dòng món;
  - xuất xong vẫn gọi thêm món được, và tệp kế tiếp có món mới;
  - bàn không có tạm tính: nút nhận thông báo, mở thẳng đường dẫn thì quay về màn hình;
  - Bếp, Thu ngân và khách bị chặn.

Thuật toán tạo PDF (bố cục, font rút gọn, ToUnicode) đã được chạy thử bằng một bản viết lại tương đương. Kết quả kiểm bằng `pdfinfo`, `pdffonts` và `pdftotext`: một trang, 226,77pt (80mm), font nhúng kèm Unicode, chữ tiếng Việt tách ra đúng. Bản ảnh render từ PDF cũng giống bản in.
