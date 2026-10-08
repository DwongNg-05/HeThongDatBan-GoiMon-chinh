# S2-01 Task 4: Tối ưu hiển thị trên điện thoại và hiệu năng

Khách xem và tìm món trên màn hình từ 360px trở lên mà bố cục không bị tràn hay che nội dung. Với 200 món, trang thực đơn tải dưới 2 giây và vẫn tìm kiếm được.

**AC hoàn tất:** AC1, AC2, AC3, AC4. Story S2-01 không còn AC nào chưa làm.

## Bố cục trên điện thoại (360px trở lên)

Các thay đổi nằm trong `wwwroot/css/public-menu.css`:

- **Màn hình ≤ 400px:** ảnh món 72px; khoảng cách, cỡ chữ tiêu đề, phần đầu trang và các nút nhóm món đều thu gọn lại. Ô tìm kiếm vẫn cao 44px để dễ chạm.
- **Chữ dài:** tên món, tên nhóm, từ khoá tìm kiếm và thông báo không tìm thấy được phép xuống dòng (`overflow-wrap: anywhere`), kể cả từ rất dài. Giá và đơn vị tính tự xuống dòng khi không đủ chỗ.
- **Thanh nhóm món:** cuộn ngang và luôn dính ở đầu màn hình. Mô tả món hiện tối đa 3 dòng.
- **Nhãn “Tạm hết”:** không bị làm mờ và không đè lên ảnh hay tên món.

Mình đã kiểm tra bằng Chromium với trang dựng từ đúng CSS của dự án và 200 món, ở các bề rộng 360px, 390px, 412px và 768px. Kết quả:
- trang không cuộn ngang;
- không phần tử nào nằm ngoài màn hình;
- ô tìm kiếm rộng 275px ở màn hình 360px;
- nhãn “Tạm hết” vẫn rõ khi món bị làm mờ;
- trang kết quả tìm kiếm cũng không tràn ngang.

## Giảm dữ liệu khi mở trang thực đơn

| Thay đổi | Nơi sửa | Hiệu quả |
| --- | --- | --- |
| Không tải jQuery và Bootstrap JS cho khách chưa đăng nhập | `_Layout.cshtml` (cờ `ViewData["LightweightScripts"]` do `Views/Menu/Index.cshtml` đặt) | Bớt khoảng 170 KB JavaScript chưa nén. Nhân viên đã đăng nhập vẫn có Bootstrap JS cho menu điều hướng. |
| Nén HTML/JSON bằng Brotli hoặc Gzip cho `/Menu` và `/api/menu` | `Program.cs` (`AddResponseCompression` + `UseWhen`) | HTML 200 món chỉ còn khoảng 1/5 đến 1/10 dung lượng. Chỉ nén khi khách **chưa đăng nhập**: trang có hiện lại từ khoá tìm kiếm, nên không nén trang có thể chứa token của nhân viên (tránh lỗ hổng BREACH). |
| Rút gọn mô tả còn tối đa 140 ký tự trong HTML | `PublicMenuDish.DescriptionPreview`, `MenuText.Preview` | Màn hình chỉ hiện 3 dòng nên không gửi phần thừa. API vẫn trả mô tả đầy đủ. |
| Chỉ tải ngay 4 ảnh đầu, các ảnh còn lại tải khi cuộn tới | `Views/Menu/Index.cshtml` (`loading="eager|lazy"`, kích thước ảnh khớp CSS) | Lần mở đầu tiên không phải tải ảnh của 200 món. |
| Bỏ qua việc vẽ các nhóm món nằm ngoài màn hình | `content-visibility: auto` trên `.public-menu-group` | Trình duyệt điện thoại hiển thị trang 200 món nhanh hơn. |

CSS, JS và ảnh có sẵn tiếp tục được phục vụ qua `MapStaticAssets`: đã nén sẵn và có dấu phiên bản để trình duyệt lưu đệm.

## Dữ liệu kiểm thử 200 món

```powershell
dotnet run --project tools/RestaurantManagement.DbTool -- seed-menu-200   # nạp/mở bán 200 món mẫu
dotnet run --project tools/RestaurantManagement.DbTool -- hide-menu-200   # ẩn lại sau khi demo
```

- Seed nằm ở `database/seeds/MenuLoadTest200.sql`, gồm 200 món chia đều cho 8 nhóm. Ngoài 5 nhóm sẵn có, seed thêm 3 nhóm: *Món nướng*, *Hải sản*, *Món chay*.
- Trong 200 món có 20 món hết trong ngày, 8 món có tên và mô tả dài (để kiểm tra bố cục 360px) và vài món giá 7 chữ số.
- Tên món có hậu tố `(mẫu 001)` … `(mẫu 200)`.
- Chạy lại nhiều lần không bị trùng.
- `hide-menu-200` chỉ **ẩn** món (đặt `IsActive = 0`), không xoá, vì món có thể đã nằm trong đơn hàng. Lệnh này cũng ẩn 3 nhóm mới nếu nhóm không còn món nào khác.
- Không chạy các lệnh này trên database sản xuất.

## Đo thời gian và kiểm tra

```powershell
dotnet build RestaurantManagement.sln -m:1
dotnet run --project tests/RestaurantManagement.AreaTests --no-build
dotnet run --no-build --project tools/RestaurantManagement.DbTool -- verify
```

### `MenuPerformanceTests` (AreaTests, không cần database)

- Rút gọn mô tả đúng ranh giới từ.
- Dựng thực đơn 200 món trong 8 nhóm, giữ đủ 20 món tạm hết. Thời gian dựng dưới 500 ms.
- Tìm `com rang` trong 200 món ra đúng 8 món, mất dưới 200 ms.
- Tìm một món đang tạm hết trong 200 món vẫn ra kết quả.

### `MenuPerformanceVerification` (lệnh `verify`, SQL Server thật, khách chưa đăng nhập)

- Nạp 200 món hai lần và kiểm tra không bị trùng.
- **Đo 5 lần tải `/Menu`.** Mỗi lần gồm HTML cộng toàn bộ CSS/JS/ảnh mà trang dùng, tải song song giống trình duyệt. Lần chậm nhất phải dưới **2000 ms**. Kết quả in ra dạng `INFO: ... in ms: ...`.
- Trang hiển thị đủ 200 món, nhãn “Tạm hết”, chỉ 4 ảnh tải ngay.
- Không tải jQuery hay Bootstrap JS, mô tả dài đã được rút gọn, HTML được nén (in ra dung lượng trước và sau khi nén).
- **Tìm kiếm trong 200 món:** thử `com rang`, `BO LUC LAC`, `mau 150`. Kết quả phải đúng, trang tải lại dưới 2 giây, và món tạm hết vẫn có nhãn.
- Cuối cùng chạy `hide-menu-200`, để các bước kiểm tra sau đó không bị ảnh hưởng.

### `tools/tests/menu-mobile.check.cjs` (trình duyệt thật, màn hình 360px và 412px)

Script mở trang trên ứng dụng đang chạy, dùng Edge hoặc Chrome có sẵn trên máy, nên không phải tải trình duyệt riêng.

Script kiểm tra:
- thời gian tải tới sự kiện `load` dưới 2 giây;
- đủ 200 món;
- không tràn ngang;
- ô tìm kiếm đủ lớn để chạm và gõ;
- nhãn “Tạm hết” hiển thị rõ;
- gõ `com rang`, bấm **Tìm**, rồi đo thời gian tải lại và kiểm tra kết quả.

Script lưu ảnh chụp vào `menu-360.png` và `menu-412.png`.

```powershell
npm install --no-save playwright-core
dotnet run --project tools/RestaurantManagement.DbTool -- seed-menu-200
dotnet run --project src/RestaurantManagement.Web --urls http://localhost:5105
# mở cửa sổ PowerShell khác:
node tools/tests/menu-mobile.check.cjs
```

## Demo

1. Chạy `seed-menu-200` rồi mở web.
2. Trong Chrome hoặc Edge, nhấn **F12**, rồi **Ctrl+Shift+M** để giả lập điện thoại, chọn bề rộng **360**. Mở `/Menu`:
   - trang không có thanh cuộn ngang;
   - ảnh, tên, mô tả, giá và nhãn “Tạm hết” nằm gọn trong màn hình.
3. Xem thời gian tải trang ở tab **Network**: dòng *Load* ở cuối bảng phải dưới 2 giây. Hoặc chạy `node tools/tests/menu-mobile.check.cjs`.
4. Gõ `com rang` rồi bấm **Tìm**. Kết quả hiện ngay, trang vẫn gọn ở bề rộng 360px.
5. Demo xong, chạy `hide-menu-200`.
