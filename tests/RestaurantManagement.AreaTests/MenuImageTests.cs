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
        new FormFile(new MemoryStream(bytes), 0, length ?? bytes.Length, "ImageFile", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };

    internal static async Task Run(Action<bool, string> check)
    {
        check(DishImageRules.Validate(File(Png, "goi-cuon.png")) is { IsValid: true, Extension: ".png" }, "Dish image: PNG accepted");
        check(DishImageRules.Validate(File(Jpeg, "bo.JPG")) is { IsValid: true, Extension: ".jpg" }, "Dish image: JPG accepted (case-insensitive)");
        check(DishImageRules.Validate(File(Jpeg, "bo.jpeg")) is { IsValid: true, Extension: ".jpg" }, "Dish image: .jpeg stored as .jpg");
        check(DishImageRules.Validate(File([0x47, 0x49, 0x46, 0x38, 0x39, 0x61], "a.gif")).Error == DishImageRules.FormatError, "Dish image: GIF rejected");
        check(DishImageRules.Validate(File(Png, "a.webp")).Error == DishImageRules.FormatError, "Dish image: other extension rejected even with image content");
        check(DishImageRules.Validate(File("<script>"u8.ToArray(), "fake.png", "image/png")).Error == DishImageRules.ContentError, "Dish image: renamed non-image rejected");
        check(DishImageRules.Validate(File(Jpeg, "mismatch.png")).Error == DishImageRules.ContentError, "Dish image: JPG content with .png name rejected");
        check(DishImageRules.Validate(File([], "empty.png")).Error == DishImageRules.EmptyFileError, "Dish image: empty file rejected");
        check(DishImageRules.Validate(File(Png, "big.png", length: DishImageRules.MaxBytes + 1)).Error == DishImageRules.TooLargeError, "Dish image: file over 5 MB rejected");
        check(DishImageRules.Validate(File(Png, "max.png", length: DishImageRules.MaxBytes)).IsValid, "Dish image: exactly 5 MB accepted");

        var folder = Path.Combine(Path.GetTempPath(), "rm-anh-mon-" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new DiskDishImageStorage(folder);
            var path = await storage.SaveAsync(File(Png, "../../evil name.png"));
            var fileName = path[DishImageRules.WebFolder.Length..];
            check(path.StartsWith("/uploads/mon-an/") && path.EndsWith(".png") && !path.Contains("evil") && !fileName.Contains('/'),
                "Dish image: saved under /uploads/mon-an with a random name, ignoring client file name");
            check(System.IO.File.ReadAllBytes(Path.Combine(folder, fileName)).SequenceEqual(Png), "Dish image: file content stored unchanged");
            storage.Delete("/images/thuc-don/lau.svg");
            storage.Delete("/uploads/mon-an/../../appsettings.json");
            check(System.IO.File.Exists(Path.Combine(folder, fileName)), "Dish image: delete ignores paths outside upload folder");
            storage.Delete(path);
            check(!System.IO.File.Exists(Path.Combine(folder, fileName)), "Dish image: uploaded file can be removed");

            // Tạo món qua trang Tạo món: đường dẫn ảnh do máy chủ tạo được lưu vào món.
            var store = new InMemoryMenuStore();
            var category = store.GetAllCategories().First();
            var page = new RestaurantManagement.Web.Pages.Dishes.CreateModel(store, storage)
            {
                Dish = new Dish
                {
                    Name = "Chả giò", CategoryId = category.Id, PriceVnd = 55000, Unit = "Phần",
                    ShortDescription = "Giòn rụm", PrepMinutes = 10, ImagePath = "javascript:alert(1)"
                },
                ImageFile = File(Jpeg, "cha-gio.jpg", "image/jpeg")
            };
            check(await page.OnPostAsync(CancellationToken.None) is RedirectToPageResult, "Dish image: create dish with JPG succeeds");
            var created = store.GetAllDishes().Single(m => m.Name == "Chả giò");
            check(created.ImagePath is { } p && p.StartsWith("/uploads/mon-an/") && p.EndsWith(".jpg")
                && System.IO.File.Exists(Path.Combine(folder, p[DishImageRules.WebFolder.Length..])),
                "Dish image: stored dish keeps uploaded image path, not the forged form value");
            check(store.GetPublicMenu().SelectMany(n => n.Dishes).Single(m => m.Id == created.Id).ImageUrl == created.ImagePath,
                "Dish image: public menu shows the uploaded image");

            var noImage = new RestaurantManagement.Web.Pages.Dishes.CreateModel(store, storage)
            {
                Dish = new Dish { Name = "Nem nướng", CategoryId = category.Id, PriceVnd = 60000, Unit = "Phần", PrepMinutes = 10 }
            };
            check(await noImage.OnPostAsync(CancellationToken.None) is RedirectToPageResult
                && store.GetAllDishes().Single(m => m.Name == "Nem nướng").ImagePath is null, "Dish image: image is optional when creating a dish");

            var filesBefore = Directory.GetFiles(folder).Length;
            var invalid = new RestaurantManagement.Web.Pages.Dishes.CreateModel(store, storage)
            {
                Dish = new Dish { Name = "Món GIF", CategoryId = category.Id, PriceVnd = 60000, Unit = "Phần", PrepMinutes = 10 },
                ImageFile = File([0x47, 0x49, 0x46, 0x38, 0x39, 0x61], "image.gif", "image/gif")
            };
            check(await invalid.OnPostAsync(CancellationToken.None) is PageResult
                && invalid.ModelState[nameof(invalid.ImageFile)]?.Errors.Single().ErrorMessage == DishImageRules.FormatError
                && store.GetAllDishes().All(m => m.Name != "Món GIF") && Directory.GetFiles(folder).Length == filesBefore,
                "Dish image: invalid image shows error, saves neither dish nor file");
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }
}
