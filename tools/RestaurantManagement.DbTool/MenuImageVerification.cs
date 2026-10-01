using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace RestaurantManagement.DbTool;

/// <summary>
/// Tạo/sửa món có ảnh qua giao diện web với SQL Server thật: chỉ JPG/PNG, đường dẫn ảnh ghi vào MenuItems.ImagePath.
/// Client phải đang đăng nhập bằng quản lý.
/// </summary>
internal static class MenuImageVerification
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xD9];
    private static readonly string UploadRoot = Path.Combine(DatabaseTool.Root, "src", "RestaurantManagement.Web", "wwwroot", "uploads", "mon-an");

    internal static async Task Run(string connection, HttpClient client)
    {
        var khaiVi = await Scalar<int>(connection, "SELECT Id FROM dbo.MenuCategories WHERE Name=N'Khai vị'");
        var savedFiles = new List<string>();
        try
        {
            using (var invalid = await Create(client, khaiVi, "S2 món ảnh GIF", ("anh.gif", "image/gif", new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 })))
            {
                var html = WebUtility.HtmlDecode(await invalid.Content.ReadAsStringAsync());
                Assert(invalid.StatusCode == HttpStatusCode.OK && html.Contains("Chỉ chấp nhận ảnh định dạng JPG hoặc PNG."), "GIF upload rejected with message");
            }
            using (var fake = await Create(client, khaiVi, "S2 món ảnh giả", ("anh.png", "image/png", "not an image"u8.ToArray())))
                Assert(fake.StatusCode == HttpStatusCode.OK && WebUtility.HtmlDecode(await fake.Content.ReadAsStringAsync()).Contains("Nội dung tệp không phải ảnh JPG/PNG hợp lệ."), "Renamed non-image rejected");
            await Check(connection, "SELECT CASE WHEN NOT EXISTS(SELECT 1 FROM dbo.MenuItems WHERE Name IN (N'S2 món ảnh GIF',N'S2 món ảnh giả')) THEN 1 ELSE 0 END", "Rejected uploads create no dish");

            using (var png = await Create(client, khaiVi, "S2 món có ảnh PNG", ("goi-cuon.png", "image/png", Png)))
                Assert(png.StatusCode == HttpStatusCode.Redirect, "Create dish with PNG image");
            using (var jpg = await Create(client, khaiVi, "S2 món có ảnh JPG", ("cha gio.jpg", "image/jpeg", Jpeg)))
                Assert(jpg.StatusCode == HttpStatusCode.Redirect, "Create dish with JPG image");

            var pngPath = await Scalar<string>(connection, "SELECT ImagePath FROM dbo.MenuItems WHERE Name=N'S2 món có ảnh PNG'");
            var jpgPath = await Scalar<string>(connection, "SELECT ImagePath FROM dbo.MenuItems WHERE Name=N'S2 món có ảnh JPG'");
            savedFiles.Add(pngPath);
            savedFiles.Add(jpgPath);
            Assert(Regex.IsMatch(pngPath, "^/uploads/mon-an/[0-9a-f]{32}\\.png$") && Regex.IsMatch(jpgPath, "^/uploads/mon-an/[0-9a-f]{32}\\.jpg$"),
                "Database stores the uploaded image path (random name, .png/.jpg), not the forged form value");

            using (var anonymous = new HttpClient { BaseAddress = client.BaseAddress, Timeout = client.Timeout })
            {
                using var image = await anonymous.GetAsync(pngPath);
                Assert(image.StatusCode == HttpStatusCode.OK && image.Content.Headers.ContentType?.MediaType == "image/png"
                    && (await image.Content.ReadAsByteArrayAsync()).SequenceEqual(Png), "Uploaded image served to customers without login");
                var menu = WebUtility.HtmlDecode(await anonymous.GetStringAsync("/ThucDon"));
                Assert(menu.Contains($"src=\"{pngPath}\"") && menu.Contains($"src=\"{jpgPath}\""), "Public menu shows uploaded images");
            }

            // Sửa món: thay ảnh PNG bằng JPG, ảnh cũ được dọn.
            var id = await Scalar<int>(connection, "SELECT Id FROM dbo.MenuItems WHERE Name=N'S2 món có ảnh PNG'");
            var editHtml = await client.GetStringAsync($"/QuanLyMon/Sua/{id}");
            Assert(WebUtility.HtmlDecode(editHtml).Contains($"src=\"{pngPath}\""), "Edit form shows current image");
            using (var form = Form(Token(editHtml), khaiVi, "S2 món có ảnh PNG", ("moi.jpg", "image/jpeg", Jpeg)))
            {
                form.Add(new StringContent(id.ToString()), "Mon.Id");
                using var edited = await client.PostAsync($"/QuanLyMon/Sua/{id}", form);
                Assert(edited.StatusCode == HttpStatusCode.Redirect, "Edit dish replaces image");
            }
            var replaced = await Scalar<string>(connection, $"SELECT ImagePath FROM dbo.MenuItems WHERE Id={id}");
            savedFiles.Add(replaced);
            Assert(replaced != pngPath && replaced.EndsWith(".jpg") && !File.Exists(LocalPath(pngPath)) && File.Exists(LocalPath(replaced)),
                "Replaced image path saved and old upload removed");

            var editAgain = await client.GetStringAsync($"/QuanLyMon/Sua/{id}");
            using (var keep = Form(Token(editAgain), khaiVi, "S2 món có ảnh PNG", null))
            {
                keep.Add(new StringContent(id.ToString()), "Mon.Id");
                using var kept = await client.PostAsync($"/QuanLyMon/Sua/{id}", keep);
                Assert(kept.StatusCode == HttpStatusCode.Redirect, "Edit without new image succeeds");
            }
            Assert(await Scalar<string>(connection, $"SELECT ImagePath FROM dbo.MenuItems WHERE Id={id}") == replaced, "Edit without new image keeps current image");
            Console.WriteLine("PASS: dish image upload checks.");
        }
        finally
        {
            // Món kiểm thử không đổi giá nên không có lịch sử giá (bảng lịch sử giá chỉ cho thêm, S1-05 Task 3).
            await DatabaseTool.Execute(connection, "DELETE dbo.MenuItems WHERE Name LIKE N'S2 món%ảnh%' AND NOT EXISTS(SELECT 1 FROM dbo.MenuPriceHistory h WHERE h.MenuItemId=dbo.MenuItems.Id);");
            foreach (var path in savedFiles) { try { File.Delete(LocalPath(path)); } catch (IOException) { } }
        }
    }

    private static async Task<HttpResponseMessage> Create(HttpClient client, int categoryId, string name, (string FileName, string ContentType, byte[] Bytes) image)
    {
        var html = await client.GetStringAsync("/QuanLyMon/Tao");
        using var form = Form(Token(html), categoryId, name, image);
        form.Add(new StringContent("javascript:alert(1)"), "Mon.DuongDanAnh");
        return await client.PostAsync("/QuanLyMon/Tao", form);
    }

    private static MultipartFormDataContent Form(string token, int categoryId, string name, (string FileName, string ContentType, byte[] Bytes)? image)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { new StringContent(name), "Mon.Ten" },
            { new StringContent(categoryId.ToString()), "Mon.NhomMonId" },
            { new StringContent("50000"), "Mon.GiaBanVnd" },
            { new StringContent("Phần"), "Mon.DonViTinh" },
            { new StringContent("Món kiểm thử ảnh"), "Mon.MoTaNgan" },
            { new StringContent("10"), "Mon.ThoiGianCheBienPhut" },
            { new StringContent("0"), "Mon.TrangThai" }
        };
        if (image is { } file)
        {
            var content = new ByteArrayContent(file.Bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
            form.Add(content, "AnhMon", file.FileName);
        }
        return form;
    }

    private static string LocalPath(string webPath) => Path.Combine(UploadRoot, Path.GetFileName(webPath));
    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);

    private static async Task<T> Scalar<T>(string connection, string sql)
    {
        await using var cn = new SqlConnection(connection);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        return (T)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task Check(string connection, string sql, string name) => Assert(await Scalar<int>(connection, sql) == 1, name);

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
    }
}
