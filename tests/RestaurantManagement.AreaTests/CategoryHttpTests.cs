using System.Net;
using System.Text.RegularExpressions;
using RestaurantManagement.Web.Services;

internal static class CategoryHttpTests
{
    internal static async Task Run(HttpClient client)
    {
        static void Check(bool ok, string label)
        {
            if (!ok) throw new Exception("FAIL: " + label);
            Console.WriteLine("PASS: " + label);
        }
        async Task<string> Get(string path) => WebUtility.HtmlDecode(await client.GetStringAsync(path));
        async Task<HttpResponseMessage> Post(string path, Dictionary<string,string> values)
        {
            var html = await Get(path);
            values["__RequestVerificationToken"] = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
            return await client.PostAsync(path, new FormUrlEncodedContent(values));
        }
        Dictionary<string,string> Create(string name) => new() { ["Ten"] = name, ["ThuTuHienThi"] = "6", ["DangSuDung"] = "true" };
        const string create = "/QuanLyNhomMon/Tao";
        const string list = "/QuanLyNhomMon";
        using (var added = await Post(create, Create("Món nướng")))
            Check(added.StatusCode == HttpStatusCode.Redirect, "Category HTTP: create succeeds");
        Check((await Get(list)).Contains("Thêm nhóm món thành công."), "Category HTTP: create success message");
        var listing = await Get(list);
        Check(listing.Contains("Món nướng"), "Category HTTP: data survives page reload");
        var row = Regex.Matches(listing, "<tr>.*?</tr>", RegexOptions.Singleline).Single(m => m.Value.Contains("Món nướng")).Value;
        var editPath = Regex.Match(row, "href=\"([^\"]*/Sua/[0-9]+)\"").Groups[1].Value;
        Check(editPath.Length > 0, "Category HTTP: edit link uses group ID");
        foreach (var (name, error) in new[] { (" MÓN   NƯỚNG ", QuyTacTenNhomMon.TenTrung), (" ", QuyTacTenNhomMon.TenRong), (new string('a',51), QuyTacTenNhomMon.TenQuaDai) })
        {
            using var rejected = await Post(create, Create(name));
            var html = WebUtility.HtmlDecode(await rejected.Content.ReadAsStringAsync());
            Check(rejected.StatusCode == HttpStatusCode.OK && html.Contains(error), "Category HTTP: create validation: " + error);
        }
        foreach (var (name, error) in new[] { ("Khai vị", QuyTacTenNhomMon.TenTrung), ("", QuyTacTenNhomMon.TenRong), (new string('b',51), QuyTacTenNhomMon.TenQuaDai) })
        {
            using var rejected = await Post(editPath, new() { ["Ten"] = name });
            Check(rejected.StatusCode == HttpStatusCode.OK && WebUtility.HtmlDecode(await rejected.Content.ReadAsStringAsync()).Contains(error), "Category HTTP: edit validation: " + error);
        }
        using (var edited = await Post(editPath, new() { ["Ten"] = "Món nướng BBQ", ["DangSuDung"] = "false", ["ThuTuHienThi"] = "999" }))
            Check(edited.StatusCode == HttpStatusCode.Redirect, "Category HTTP: rename succeeds");
        Check((await Get(list)).Contains("Sửa tên nhóm món thành công."), "Category HTTP: edit success message");
        Check((await Get(editPath)).Contains("Món nướng BBQ"), "Category HTTP: renamed value survives reload");
        listing = await Get(list);
        row = Regex.Matches(listing, "<tr>.*?</tr>", RegexOptions.Singleline).Single(m => m.Value.Contains("Món nướng BBQ")).Value;
        Check(row.Contains("Đang sử dụng") && row.Contains("<td>6</td>"), "Category HTTP: edit cannot alter status or sort order");
        foreach (var path in new[] { "/ThucDon", "/GoiMon" })
            Check((await Get(path)).Contains("Món nướng BBQ"), "Category HTTP: renamed group appears on " + path);
        using (var missing = await client.GetAsync("/QuanLyNhomMon/Sua/999999"))
            Check(missing.StatusCode == HttpStatusCode.NotFound, "Category HTTP: missing group returns 404");
        using (var noToken = await client.PostAsync(create, new FormUrlEncodedContent(Create("Không token"))))
            Check(noToken.StatusCode == HttpStatusCode.BadRequest, "Category HTTP: requests without anti-forgery token rejected");
        const string orderPath = "/QuanLyNhomMon/SapXep";
        var orderHtml = await Get(orderPath);
        var ids = Regex.Matches(orderHtml, "name=\"ThuTu\" value=\"([0-9]+)\"").Select(m => m.Groups[1].Value).ToArray();
        Check(ids.Length == 6 && orderHtml.Contains("Lưu thứ tự") && orderHtml.Contains("category-order.js"), "Order HTTP: ordering page contains all groups and controls");
        async Task<HttpResponseMessage> SaveOrder(string[] order, string[] baseline)
        {
            var html = await Get(orderPath);
            var values = order.Select(id => new KeyValuePair<string,string>("ThuTu", id)).ToList();
            values.AddRange(baseline.Select(id => new KeyValuePair<string,string>("BanDau", id)));
            values.Add(new("__RequestVerificationToken", Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value));
            return await client.PostAsync(orderPath, new FormUrlEncodedContent(values));
        }
        var drinksFirst = new[] { "5" }.Concat(ids.Where(id => id != "5")).ToArray();
        using (var savedOrder = await SaveOrder(drinksFirst, ids))
            Check(savedOrder.StatusCode == HttpStatusCode.Redirect, "Order HTTP: save drinks first");
        Check((await Get(list)).Contains("Lưu thứ tự nhóm món thành công."), "Order HTTP: save success message");
        var reloaded = await Get(orderPath);
        Check(Regex.Matches(reloaded, "name=\"ThuTu\" value=\"([0-9]+)\"").Select(m => m.Groups[1].Value).SequenceEqual(drinksFirst), "Order HTTP: persisted order after reload");
        foreach (var path in new[] { "/ThucDon", "/GoiMon" })
        {
            var html = await Get(path);
            Check(Regex.Matches(html, "data-nhom-id=\"([0-9]+)\"").Select(m => m.Groups[1].Value).SequenceEqual(drinksFirst), "Order HTTP: exact saved order on " + path);
        }
        using (var invalid = await SaveOrder(new[] { "5", "5" }, drinksFirst))
            Check(WebUtility.HtmlDecode(await invalid.Content.ReadAsStringAsync()).Contains("Thứ tự không hợp lệ"), "Order HTTP: duplicate/missing group rejected");
        using (var staleOrder = await SaveOrder(ids, ids))
            Check(WebUtility.HtmlDecode(await staleOrder.Content.ReadAsStringAsync()).Contains("đã thay đổi"), "Order HTTP: stale form rejected");
        using (var lastOrder = await SaveOrder(ids, drinksFirst))
            Check(lastOrder.StatusCode == HttpStatusCode.Redirect, "Order HTTP: restore original order");
        async Task<HttpResponseMessage> ChangeStatus(string id, string handler)
        {
            var path = "/QuanLyNhomMon/TrangThai/" + id;
            var html = await Get(path);
            var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
            return await client.PostAsync(path + "?handler=" + handler, new FormUrlEncodedContent(new Dictionary<string,string> { ["__RequestVerificationToken"] = token }));
        }
        foreach (var (id, name) in new[] { ("6", "Món nướng BBQ"), ("5", "Đồ uống") })
        {
            var confirm = await Get("/QuanLyNhomMon/TrangThai/" + id);
            Check(confirm.Contains(name) && confirm.Contains("vẫn được giữ nguyên"), "Lifecycle HTTP: confirmation explains retained data");
            Check((await Get("/ThucDon")).Contains("id=\"tieu-de-nhom-" + id + "\""), "Lifecycle HTTP: GET confirmation does not deactivate");
            using (var stopped = await ChangeStatus(id, "Ngung"))
                Check(stopped.StatusCode == HttpStatusCode.Redirect, "Lifecycle HTTP: stop group " + id);
            var admin = await Get(list);
            Check(admin.Contains(name) && admin.Contains("Đã ngừng sử dụng nhóm món"), "Lifecycle HTTP: admin retains group and reports success");
            foreach (var path in new[] { "/ThucDon", "/GoiMon" })
                Check(!(await Get(path)).Contains("id=\"tieu-de-nhom-" + id + "\""), "Lifecycle HTTP: group hidden on " + path);
            if (id == "5")
                Check((await Get("/QuanLyMon/Sua/5")).Contains("Trà đào"), "Lifecycle HTTP: linked dish still available in management");
            using (var resumed = await ChangeStatus(id, "BatLai"))
                Check(resumed.StatusCode == HttpStatusCode.Redirect, "Lifecycle HTTP: reactivate group " + id);
            foreach (var path in new[] { "/ThucDon", "/GoiMon" })
                Check((await Get(path)).Contains("id=\"tieu-de-nhom-" + id + "\""), "Lifecycle HTTP: reactivated group visible on " + path);
        }
        using (var unknownStatus = await client.GetAsync("/QuanLyNhomMon/TrangThai/999999"))
            Check(unknownStatus.StatusCode == HttpStatusCode.NotFound, "Lifecycle HTTP: unknown group returns 404");
        using (var noStatusToken = await client.PostAsync("/QuanLyNhomMon/TrangThai/5?handler=Ngung", new FormUrlEncodedContent(new Dictionary<string,string>())))
            Check(noStatusToken.StatusCode == HttpStatusCode.BadRequest, "Lifecycle HTTP: status changes require anti-forgery token");
        var blockedDelete = await Get("/QuanLyNhomMon/Xoa/3");
        Check(blockedDelete.Contains("Không thể xóa") && blockedDelete.Contains("Chuyển sang nhóm khác"), "Delete HTTP: populated group shows transfer instructions");
        // Use a token from another page to verify the server guard even without a delete button.
        var tokenHtml = await Get(create);
        var deleteToken = Regex.Match(tokenHtml, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        using (var forced = await client.PostAsync("/QuanLyNhomMon/Xoa/3", new FormUrlEncodedContent(new Dictionary<string,string> { ["__RequestVerificationToken"] = deleteToken })))
            Check(forced.StatusCode == HttpStatusCode.OK && WebUtility.HtmlDecode(await forced.Content.ReadAsStringAsync()).Contains("Không thể xóa"), "Delete HTTP: forced POST cannot delete populated group");
        var emptyConfirm = await Get("/QuanLyNhomMon/Xoa/6");
        Check(emptyConfirm.Contains("Xác nhận xóa vĩnh viễn") && emptyConfirm.Contains("Hủy") && (await Get(list)).Contains("Món nướng BBQ"), "Delete HTTP: viewing/cancelling confirmation keeps group");
        using (var removed = await Post("/QuanLyNhomMon/Xoa/6", new()))
            Check(removed.StatusCode == HttpStatusCode.Redirect, "Delete HTTP: empty group deleted");
        Check((await Get(list)).Contains("Xóa nhóm món thành công."), "Delete HTTP: success message");
        Check(!(await Get(list)).Contains("Món nướng BBQ"), "Delete HTTP: deleted group absent after reload");
        using (var moved = await Post("/QuanLyMon/Sua/3", new() { ["Mon.Id"] = "3", ["Mon.Ten"] = "Lẩu Thái", ["Mon.NhomMonId"] = "2", ["Mon.GiaBanVnd"] = "250000", ["Mon.DonViTinh"] = "Nồi", ["Mon.MoTaNgan"] = "Món chuyển nhóm", ["Mon.ThoiGianCheBienPhut"] = "15", ["Mon.TrangThai"] = "DangBan" }))
            Check(moved.StatusCode == HttpStatusCode.Redirect, "Delete HTTP: transfer dish via existing editor");
        using (var removedSoup = await Post("/QuanLyNhomMon/Xoa/3", new()))
            Check(removedSoup.StatusCode == HttpStatusCode.Redirect, "Delete HTTP: delete after moving all dishes");
        foreach (var path in new[] { "/ThucDon", "/GoiMon" })
        {
            var html = await Get(path);
            Check(!html.Contains("id=\"tieu-de-nhom-3\"") && html.Contains("Lẩu Thái"), "Delete HTTP: removed group hidden and moved dish retained on " + path);
        }
        using (var missingDelete = await client.GetAsync("/QuanLyNhomMon/Xoa/3"))
            Check(missingDelete.StatusCode == HttpStatusCode.NotFound, "Delete HTTP: deleted ID returns 404");
        using (var unverifiedDelete = await client.PostAsync("/QuanLyNhomMon/Xoa/2", new FormUrlEncodedContent(new Dictionary<string,string>())))
            Check(unverifiedDelete.StatusCode == HttpStatusCode.BadRequest, "Delete HTTP: token required");
    }
}
