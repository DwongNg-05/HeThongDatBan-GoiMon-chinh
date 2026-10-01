using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Data.Models;
using RestaurantManagement.Web.Services;

/// <summary>Tải ảnh món khi tạo món: chỉ JPG/PNG, tối đa 5 MB, đường dẫn ảnh được ghi vào dữ liệu món.</summary>
internal static class MenuImageTests
{
    internal static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
    internal static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xD9];

    internal static IFormFile File(byte[] bytes, string fileName, string contentType = "application/octet-stream", long? length = null) =>
        new FormFile(new MemoryStream(bytes), 0, length ?? bytes.Length, "AnhMon", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };

    internal static async Task Run(Action<bool, string> check)
    {
        check(QuyTacAnhMonAn.KiemTra(File(Png, "goi-cuon.png")) is { HopLe: true, Duoi: ".png" }, "Dish image: PNG accepted");
        check(QuyTacAnhMonAn.KiemTra(File(Jpeg, "bo.JPG")) is { HopLe: true, Duoi: ".jpg" }, "Dish image: JPG accepted (case-insensitive)");
        check(QuyTacAnhMonAn.KiemTra(File(Jpeg, "bo.jpeg")) is { HopLe: true, Duoi: ".jpg" }, "Dish image: .jpeg stored as .jpg");
        check(QuyTacAnhMonAn.KiemTra(File([0x47, 0x49, 0x46, 0x38, 0x39, 0x61], "a.gif")).Loi == QuyTacAnhMonAn.LoiDinhDang, "Dish image: GIF rejected");
        check(QuyTacAnhMonAn.KiemTra(File(Png, "a.webp")).Loi == QuyTacAnhMonAn.LoiDinhDang, "Dish image: other extension rejected even with image content");
        check(QuyTacAnhMonAn.KiemTra(File("<script>"u8.ToArray(), "fake.png", "image/png")).Loi == QuyTacAnhMonAn.LoiNoiDung, "Dish image: renamed non-image rejected");
        check(QuyTacAnhMonAn.KiemTra(File(Jpeg, "mismatch.png")).Loi == QuyTacAnhMonAn.LoiNoiDung, "Dish image: JPG content with .png name rejected");
        check(QuyTacAnhMonAn.KiemTra(File([], "empty.png")).Loi == QuyTacAnhMonAn.LoiRong, "Dish image: empty file rejected");
        check(QuyTacAnhMonAn.KiemTra(File(Png, "big.png", length: QuyTacAnhMonAn.KichThuocToiDa + 1)).Loi == QuyTacAnhMonAn.LoiQuaLon, "Dish image: file over 5 MB rejected");
        check(QuyTacAnhMonAn.KiemTra(File(Png, "max.png", length: QuyTacAnhMonAn.KichThuocToiDa)).HopLe, "Dish image: exactly 5 MB accepted");

        var thuMuc = Path.Combine(Path.GetTempPath(), "rm-anh-mon-" + Guid.NewGuid().ToString("N"));
        try
        {
            var kho = new KhoAnhMonAnTrenDia(thuMuc);
            var duongDan = await kho.LuuAsync(File(Png, "../../evil name.png"));
            var tenTep = duongDan[QuyTacAnhMonAn.ThuMucWeb.Length..];
            check(duongDan.StartsWith("/uploads/mon-an/") && duongDan.EndsWith(".png") && !duongDan.Contains("evil") && !tenTep.Contains('/'),
                "Dish image: saved under /uploads/mon-an with a random name, ignoring client file name");
            check(System.IO.File.ReadAllBytes(Path.Combine(thuMuc, tenTep)).SequenceEqual(Png), "Dish image: file content stored unchanged");
            kho.Xoa("/images/thuc-don/lau.svg");
            kho.Xoa("/uploads/mon-an/../../appsettings.json");
            check(System.IO.File.Exists(Path.Combine(thuMuc, tenTep)), "Dish image: delete ignores paths outside upload folder");
            kho.Xoa(duongDan);
            check(!System.IO.File.Exists(Path.Combine(thuMuc, tenTep)), "Dish image: uploaded file can be removed");

            // Tạo món qua trang Tạo món: đường dẫn ảnh do máy chủ tạo được lưu vào món.
            var store = new InMemoryQuanLyMonStore();
            var nhom = store.LayTatCaNhomMon().First();
            var page = new RestaurantManagement.Web.Pages.QuanLyMon.TaoModel(store, kho)
            {
                Mon = new MonAn
                {
                    Ten = "Chả giò", NhomMonId = nhom.Id, GiaBanVnd = 55000, DonViTinh = "Phần",
                    MoTaNgan = "Giòn rụm", ThoiGianCheBienPhut = 10, DuongDanAnh = "javascript:alert(1)"
                },
                AnhMon = File(Jpeg, "cha-gio.jpg", "image/jpeg")
            };
            check(await page.OnPostAsync(CancellationToken.None) is RedirectToPageResult, "Dish image: create dish with JPG succeeds");
            var created = store.LayTatCaMonAn().Single(m => m.Ten == "Chả giò");
            check(created.DuongDanAnh is { } p && p.StartsWith("/uploads/mon-an/") && p.EndsWith(".jpg")
                && System.IO.File.Exists(Path.Combine(thuMuc, p[QuyTacAnhMonAn.ThuMucWeb.Length..])),
                "Dish image: stored dish keeps uploaded image path, not the forged form value");
            check(store.LayThucDonCongKhai().SelectMany(n => n.MonAn).Single(m => m.Id == created.Id).AnhUrl == created.DuongDanAnh,
                "Dish image: public menu shows the uploaded image");

            var noImage = new RestaurantManagement.Web.Pages.QuanLyMon.TaoModel(store, kho)
            {
                Mon = new MonAn { Ten = "Nem nướng", NhomMonId = nhom.Id, GiaBanVnd = 60000, DonViTinh = "Phần", ThoiGianCheBienPhut = 10 }
            };
            check(await noImage.OnPostAsync(CancellationToken.None) is RedirectToPageResult
                && store.LayTatCaMonAn().Single(m => m.Ten == "Nem nướng").DuongDanAnh is null, "Dish image: image is optional when creating a dish");

            var filesBefore = Directory.GetFiles(thuMuc).Length;
            var invalid = new RestaurantManagement.Web.Pages.QuanLyMon.TaoModel(store, kho)
            {
                Mon = new MonAn { Ten = "Món GIF", NhomMonId = nhom.Id, GiaBanVnd = 60000, DonViTinh = "Phần", ThoiGianCheBienPhut = 10 },
                AnhMon = File([0x47, 0x49, 0x46, 0x38, 0x39, 0x61], "anh.gif", "image/gif")
            };
            check(await invalid.OnPostAsync(CancellationToken.None) is PageResult
                && invalid.ModelState[nameof(invalid.AnhMon)]?.Errors.Single().ErrorMessage == QuyTacAnhMonAn.LoiDinhDang
                && store.LayTatCaMonAn().All(m => m.Ten != "Món GIF") && Directory.GetFiles(thuMuc).Length == filesBefore,
                "Dish image: invalid image shows error, saves neither dish nor file");
        }
        finally
        {
            try { Directory.Delete(thuMuc, recursive: true); } catch (IOException) { }
        }
    }
}
