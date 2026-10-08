using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using RestaurantManagement.Data.Models;
using RestaurantManagement.Web.Security;
using RestaurantManagement.Web.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

/// <summary>Tải ảnh món khi tạo món: chỉ JPG/PNG, tối đa 5 MB, đường dẫn ảnh được ghi vào dữ liệu món.</summary>
internal static class MenuImageTests
{
    internal static readonly byte[] Png = CreateImage(1, 1, png: true);
    internal static readonly byte[] Jpeg = CreateImage(1, 1, png: false);

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
            await CheckOptimized(check, storage, folder, CreateImage(640, 480, png: true), "small.png", 640, 480,
                "Dish image optimization: small PNG keeps its dimensions and reloads");
            await CheckOptimized(check, storage, folder, CreateImage(2048, 1024, png: false), "wide.jpg", 1024, 512,
                "Dish image optimization: wide image shrinks to 1024 and preserves its ratio");
            await CheckOptimized(check, storage, folder, CreateImage(900, 1800, png: true), "tall.png", 512, 1024,
                "Dish image optimization: tall image shrinks to 1024 and preserves its ratio");

            var largeJpeg = CreateImage(1800, 1200, png: false, noisy: true, jpegQuality: 100);
            var optimizedPath = await storage.SaveAsync(File(largeJpeg, "large.jpg"));
            var optimizedFile = Path.Combine(folder, Path.GetFileName(optimizedPath));
            using (var optimized = await Image.LoadAsync(optimizedFile))
            {
                var ratioError = Math.Abs((double)optimized.Width / optimized.Height - 1.5);
                check(Math.Max(optimized.Width, optimized.Height) <= DishImageRules.MaxDimension && ratioError < 0.002,
                    "Dish image optimization: both large edges shrink within 1024 and preserve ratio");
            }
            check(new FileInfo(optimizedFile).Length < largeJpeg.Length,
                "Dish image optimization: JPEG is recompressed to a smaller stored file");

            var path = await storage.SaveAsync(File(Png, "../../evil name.png"));
            var fileName = path[DishImageRules.WebFolder.Length..];
            check(path.StartsWith("/uploads/mon-an/") && path.EndsWith(".png") && !path.Contains("evil") && !fileName.Contains('/'),
                "Dish image: saved under /uploads/mon-an with a random name, ignoring client file name");
            var storedBytes = System.IO.File.ReadAllBytes(Path.Combine(folder, fileName));
            using (var storedImage = await Image.LoadAsync(Path.Combine(folder, fileName)))
                check(storedImage.Width == 1 && storedImage.Height == 1 && !storedBytes.SequenceEqual(Png),
                    "Dish image: optimized PNG is re-encoded and remains readable");
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

            var filesBeforeRemoval = Directory.GetFiles(folder).Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var removableImage = await storage.SaveAsync(File(Jpeg, "anh-can-xoa.jpg"));
            var removableDish = store.AddDish(new Dish
            {
                Name = "Món có ảnh cần xoá", CategoryId = category.Id, PriceVnd = 65000, Unit = "Phần",
                ShortDescription = "Kiểm tra xoá ảnh", PrepMinutes = 10, ImagePath = removableImage
            });
            var cancelMarkup = await System.IO.File.ReadAllTextAsync(Path.Combine(Directory.GetCurrentDirectory(),
                "src", "RestaurantManagement.Web", "Pages", "Dishes", "Edit.cshtml"));
            check(cancelMarkup.Contains("confirm('Bạn có chắc muốn xoá ảnh riêng của món này?')", StringComparison.Ordinal),
                "Dish image removal: browser asks for confirmation, so cancel submits no delete request");
            check(store.GetDish(removableDish.Id)?.ImagePath == removableImage
                && System.IO.File.Exists(Path.Combine(folder, Path.GetFileName(removableImage))),
                "Dish image removal: cancelling confirmation leaves the link and file intact");

            var removePage = EditPage(store, storage, new TestCurrentUser(isAuthenticated: true, role: "Manager"));
            var removed = removePage.OnPostRemoveImage(removableDish.Id);
            var afterRemoval = store.GetDish(removableDish.Id)!;
            check(removed is RedirectToPageResult && afterRemoval.ImagePath is null
                && !System.IO.File.Exists(Path.Combine(folder, Path.GetFileName(removableImage))),
                "Dish image removal: confirmed request clears the link and deletes the stored file");
            check(store.GetPublicMenu().SelectMany(n => n.Dishes).Single(m => m.Id == removableDish.Id).ImageUrl
                == "/images/thuc-don/khai-vi.svg", "Dish image removal: category default is displayed after deletion");

            var reupload = EditPage(store, storage, new TestCurrentUser(isAuthenticated: true, role: "Manager"));
            reupload.Dish = Copy(store.GetDish(removableDish.Id)!);
            reupload.ImageFile = File(Png, "anh-moi.png", "image/png");
            check(await reupload.OnPostAsync(CancellationToken.None) is RedirectToPageResult
                && store.GetDish(removableDish.Id)?.ImagePath is { } replacement
                && System.IO.File.Exists(Path.Combine(folder, Path.GetFileName(replacement)))
                && Directory.GetFiles(folder).Select(Path.GetFileName)
                    .Except(filesBeforeRemoval, StringComparer.OrdinalIgnoreCase).Count() == 1,
                "Dish image removal: reupload after deletion leaves only the replacement file");

            var missingFilePath = "/uploads/mon-an/00000000000000000000000000000000.jpg";
            var staleDish = store.AddDish(new Dish
            {
                Name = "Món có liên kết ảnh cũ", CategoryId = category.Id, PriceVnd = 66000, Unit = "Phần",
                ShortDescription = "Tệp đã mất", PrepMinutes = 10, ImagePath = missingFilePath
            });
            check(EditPage(store, storage, new TestCurrentUser(isAuthenticated: true, role: "Manager"))
                    .OnPostRemoveImage(staleDish.Id) is RedirectToPageResult
                && store.GetDish(staleDish.Id)?.ImagePath is null,
                "Dish image removal: stale database link can be cleared when the file is already missing");

            var unauthenticatedRemove = EditPage(store, storage, new TestCurrentUser(isAuthenticated: false, role: null));
            staleDish.ImagePath = missingFilePath;
            store.UpdateDish(staleDish);
            check(unauthenticatedRemove.OnPostRemoveImage(staleDish.Id) is UnauthorizedResult
                && store.GetDish(staleDish.Id)?.ImagePath == missingFilePath,
                "Dish image removal: unauthenticated request leaves the dish unchanged");
            check(EditPage(store, storage, new TestCurrentUser(isAuthenticated: true, role: "Waiter"))
                    .OnPostRemoveImage(staleDish.Id) is ForbidResult
                && store.GetDish(staleDish.Id)?.ImagePath == missingFilePath,
                "Dish image removal: non-manager request is forbidden");
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    private static RestaurantManagement.Web.Pages.Dishes.EditModel EditPage(InMemoryMenuStore store,
        IDishImageStorage storage, ICurrentUser user)
    {
        var page = new RestaurantManagement.Web.Pages.Dishes.EditModel(store, storage);
        var services = new ServiceCollection().AddSingleton(user).BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = services };
        page.PageContext = new PageContext
        {
            HttpContext = httpContext,
            RouteData = new RouteData(),
            ActionDescriptor = new CompiledPageActionDescriptor()
        };
        return page;
    }

    private static Dish Copy(Dish source) => new()
    {
        Id = source.Id, Name = source.Name, CategoryId = source.CategoryId, PriceVnd = source.PriceVnd,
        Unit = source.Unit, ShortDescription = source.ShortDescription, PrepMinutes = source.PrepMinutes,
        Status = source.Status, ImagePath = source.ImagePath
    };

    private sealed class TestCurrentUser(bool isAuthenticated, string? role) : ICurrentUser
    {
        public bool IsAuthenticated { get; } = isAuthenticated;
        public string? UserName => IsAuthenticated ? "test-manager" : null;
        public string? Role { get; } = role;
        public string IpAddress => "127.0.0.1";
    }

    private static async Task CheckOptimized(Action<bool, string> check, IDishImageStorage storage, string folder, byte[] input, string name,
        int expectedWidth, int expectedHeight, string label)
    {
        var path = await storage.SaveAsync(File(input, name));
        using var decoded = await Image.LoadAsync(Path.Combine(folder, Path.GetFileName(path)));
        var ratioError = Math.Abs((double)decoded.Width / decoded.Height - (double)expectedWidth / expectedHeight);
        var dimensionsAreCorrect = decoded.Width == expectedWidth && decoded.Height == expectedHeight;
        if (Math.Max(expectedWidth, expectedHeight) <= DishImageRules.MaxDimension)
            dimensionsAreCorrect &= Math.Max(decoded.Width, decoded.Height) <= DishImageRules.MaxDimension;
        check(dimensionsAreCorrect && ratioError < 0.002, label);
    }

    private static byte[] CreateImage(int width, int height, bool png, bool noisy = false, int jpegQuality = 100)
    {
        using var image = new Image<Rgb24>(width, height);
        var random = new Random(42);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var value = noisy ? (byte)random.Next(256) : (byte)90;
            image[x, y] = new Rgb24(value, noisy ? (byte)random.Next(256) : (byte)150, noisy ? (byte)random.Next(256) : (byte)210);
        }

        using var stream = new MemoryStream();
        if (png) image.SaveAsPng(stream, new PngEncoder());
        else image.SaveAsJpeg(stream, new JpegEncoder { Quality = jpegQuality });
        return stream.ToArray();
    }
}
