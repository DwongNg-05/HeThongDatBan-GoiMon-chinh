# S4-01 Task 3: in hoá đơn tạm tính ra máy in nhiệt khổ 80mm

Trên màn hình **Tạm tính** có nút **In**. Bấm nút này sẽ mở hộp thoại in của trình duyệt, với bản xem trước khổ 80mm và không có nút bấm nào trên tờ in. Chọn máy in nhiệt là in ra tờ hoá đơn gồm: phần đầu (Task 2), bảng món, nhóm món huỷ có tính tiền và tổng cộng.

**Hoàn tất:** một nửa AC 3 (in trực tiếp khổ 80mm). **Chưa làm:** xuất tệp PDF.

## Mẫu bố cục khổ 80mm (đề xuất, cần PO chốt)

| Hạng mục | Đề xuất (đã cài đặt) |
| --- | --- |
| Khổ giấy | Rộng **80mm**, lề trái/phải 4mm, nên vùng in rộng **72mm** (đúng vùng in của đa số máy in nhiệt 80mm). Chiều dài **theo nội dung**. |
| Cỡ chữ | Sans-serif (Arial). Chữ thường **12px (≈ 9pt)**. Tên quán **16px đậm**. **TỔNG CỘNG 14px đậm**. Đơn giá, ghi chú, nhãn **11px**. Chú thích cuối 10px. |
| Thứ tự in | Dòng khung **TẠM TÍNH – KHÔNG PHẢI HOÁ ĐƠN THANH TOÁN** → tên quán (căn giữa) → Bàn / Bắt đầu phục vụ / Thời điểm in / Người in → bảng món → nhóm **ĐÃ HUỶ – CÓ TÍNH TIỀN** → các dòng cộng → **TỔNG CỘNG** → “Chưa trừ giảm giá…”. |
| Cột | **Món** (phần còn lại, ≈ 42mm) \| **SL** (8mm) \| **Thành tiền** (22mm, căn phải). Đơn giá không có cột riêng (khổ hẹp): in ngay dưới tên món, dạng `65.000 đ/tô`. |
| Tên món dài | Xuống dòng trong cột **Món**. Từ quá dài, không có dấu cách, được ngắt giữa từ. Cột SL và Thành tiền giữ độ rộng cố định, nên **không bị lệch**; dòng chỉ cao thêm. Số tiền **không bao giờ xuống dòng**. Số tiền lớn nhất vừa khung là `989.010.000 đ` (99 phần × 9.990.000 đ). |
| Ghi chú | In **dòng riêng dưới tên món** (chữ nghiêng 11px), không in cùng dòng. Ghi chú được in đầy đủ, không cắt; nội dung giống trên màn hình: lựa chọn nhanh + “Ghi chú: …”. |
| Món huỷ có tính tiền | Nhóm riêng, mở đầu bằng tiêu đề in hoa **ĐÃ HUỶ – CÓ TÍNH TIỀN** có kẻ đường ngăn. Mỗi dòng trong nhóm có thêm nhãn **[Đã huỷ – có tính tiền]**. |
| Ngắt trang | Không ngắt. Cả hoá đơn là **một trang dài đúng bằng nội dung**, nên in liền một dải dù nhiều món. |

## Cách hoạt động

- **Bản in** (`_PrintReceipt.cshtml`, `_PrintLine.cshtml`):
  - Được dựng từ **cùng dữ liệu** với màn hình xem trước: cùng `TableBillPreview` và cùng phần đầu của lần mở đó (cùng thời điểm in, người in). Nhờ vậy, nội dung bản in luôn khớp màn hình.
  - Bản in bị ẩn trên màn hình.
- **`bill-print.css`** (`@media print`):
  - Chỉ giữ lại bản in. Thanh điều hướng, thanh trên, các nút và bảng xem trước đều bị ẩn.
  - Đặt lề trang bằng 0, và không cho chia đôi một dòng món.
- **`bill-print.js`**:
  - Đưa bản in ra ngay dưới `<body>`.
  - Đo chiều dài thật của bản in ở bề rộng 80mm, rồi đặt `@page { size: 80mm <dài>mm; margin: 0 }`.
  - Đo lại ngay trước khi in, khi bấm **In** hoặc Ctrl+P.
  - Nút **In** chỉ gọi `window.print()` và **không gửi yêu cầu nào lên máy chủ**. Vì vậy in xong hay huỷ hộp thoại in đều không làm đổi trạng thái bàn hay đơn gọi món, và vẫn gọi thêm món bình thường.

## Cài đặt khi in (một lần trên máy quầy)

Trong hộp thoại in của Chrome hoặc Edge:
- **Máy in:** chọn máy in nhiệt.
- **Khổ giấy:** để mặc định hoặc chọn khổ cuộn 80mm của driver; khổ giấy do trang đặt sẵn.
- **Lề:** *Không có* hoặc *Mặc định*.
- **Tỷ lệ:** **100%**.
- Bỏ chọn **Đầu trang và chân trang**.

Nếu driver chỉ có khổ cố định (ví dụ 80 × 297mm), hoá đơn dài có thể bị chia thành nhiều tờ. Khi đó nên chọn khổ cuộn hoặc “Receipt” của driver.

## Demo

1. Mở **Tạm tính** của một bàn có vài món, một món ghi chú dài, một món huỷ có tính tiền. Bấm **In**.
2. Hộp thoại in hiện bản xem trước khổ 80mm, không có nút bấm, đủ phần đầu, bảng món, nhóm **ĐÃ HUỶ – CÓ TÍNH TIỀN** và **TỔNG CỘNG**. Chọn máy in nhiệt rồi in, hoặc bấm Huỷ.
3. Bấm **← Quay lại gọi món**, rồi **Gọi thêm món**: gọi bình thường. Mở **Tạm tính** lại và bấm **In**: món mới có trên bản in.

## Kiểm thử

- **`node --test tools/tests/bill-print.test.cjs`** (đã chạy, 4/4 đạt):
  - chiều dài trang tính theo nội dung;
  - bản in được đưa ra dưới `<body>`;
  - nút **In** mở hộp thoại in;
  - đo lại trước khi in;
  - không gửi yêu cầu nào lên máy chủ.
- **`node tools/tests/bill-print-layout.check.cjs`**: chạy trên trình duyệt thật, cần `npm install --no-save playwright-core` cùng Edge hoặc Chrome. Bài này dùng CSS và JS thật, in sang PDF hai hoá đơn 3 món và 60 món, gồm tên món rất dài, tên không có dấu cách, ghi chú dài, số tiền lớn và nhóm món huỷ. Các điểm kiểm tra:
  - khi in chỉ còn bản in;
  - rộng đúng 80mm, không ô nào tràn;
  - cột SL và Thành tiền thẳng hàng;
  - tên món và ghi chú xuống dòng đúng;
  - nhãn món huỷ in rõ;
  - **một trang duy nhất**, khổ 80mm × chiều dài nội dung.

  Đã chạy trên Chromium: 20/20 đạt. Thêm `OUT_DIR=<thư mục>` để lưu PDF mẫu và xem bằng mắt.
- **`BillPrintTests.cs`** (không cần database):
  - có nút **In**;
  - bản in dựng từ cùng dữ liệu với màn hình;
  - các quy tắc CSS 80mm và các quy tắc khi in;
  - JS chỉ gọi `window.print()`;
  - hai partial bản in vẫn khớp cấu trúc mà kiểm tra trình duyệt dùng.
- **`verify-bill-print`** (SQL Server thật + web thật; chạy cùng bộ S4-01, cũng chạy trong `verify`):
  - màn hình có nút **In**, bản in, CSS và JS in;
  - **bản in khớp màn hình**: cùng phần đầu, cùng dòng món theo đúng thứ tự và thành tiền, cùng tổng; ghi chú dài gần 200 ký tự in đủ; nhóm huỷ có nhãn; món huỷ trước khi nấu không có trên bản in;
  - mở để in nhiều lần không đổi bàn, phiên hay dòng món;
  - in xong vẫn gọi thêm món được, và bản in kế tiếp có món mới.
