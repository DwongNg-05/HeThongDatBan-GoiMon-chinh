using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Controllers;
using RestaurantManagement.Data.Models;
using RestaurantManagement.Web.Services;

/// <summary>S2-01 Task 1: thực đơn công khai theo nhóm, ảnh, mô tả, giá VND, không cần đăng nhập.</summary>
internal static class PublicMenuTests
{
    internal static void Run(Action<bool, string> check)
    {
        check(DinhDangTien.Vnd(45000) == "45.000 ₫", "Public menu: VND uses dot grouping and ₫");
        check(DinhDangTien.Vnd(250000) == "250.000 ₫" && DinhDangTien.Vnd(1500000) == "1.500.000 ₫", "Public menu: six- and seven-digit VND prices");
        check(DinhDangTien.Vnd(900) == "900 ₫", "Public menu: price under one thousand has no separator");

        check(AnhMonAn.ChonAnh("/images/thuc-don/lau.svg") == "/images/thuc-don/lau.svg", "Public menu: local image path kept");
        check(AnhMonAn.ChonAnh(null) == AnhMonAn.AnhMacDinh && AnhMonAn.ChonAnh("  ") == AnhMonAn.AnhMacDinh, "Public menu: missing image uses default");
        check(new[] { "javascript:alert(1)", "//evil.example/a.png", "http://example.com/a.png", "uploads\\a.png" }
            .All(p => AnhMonAn.ChonAnh(p) == AnhMonAn.AnhMacDinh), "Public menu: unsafe image URLs replaced by default");
        check(AnhMonAn.ChonAnh("https://cdn.example.com/mon/1.jpg") == "https://cdn.example.com/mon/1.jpg", "Public menu: https image allowed");

        var store = new InMemoryQuanLyMonStore();
        string[] expected = ["Khai vị", "Món chính", "Lẩu", "Tráng miệng", "Đồ uống"];
        var menu = store.LayThucDonCongKhai();
        check(menu.Select(n => n.Ten).SequenceEqual(expected), "Public menu: groups in display order");
        check(menu.All(n => n.MonAn.Count > 0 && n.MonAn.All(m =>
                m.Ten.Length > 0 && m.MoTaNgan.Length > 0 && m.GiaVnd > 0 && m.AnhUrl.StartsWith("/images/thuc-don/")
                && m.GiaHienThi.EndsWith(" ₫") && m.DonViTinh.Length > 0)),
            "Public menu: every dish has image, name, description, VND price and unit");
        var goiCuon = menu[0].MonAn.Single();
        check(goiCuon is { Ten: "Gỏi cuốn", GiaVnd: 45000, GiaHienThi: "45.000 ₫", HetTrongNgay: false }, "Public menu: dish data matches store");

        var nhomKhaiVi = store.LayTatCaNhomMon().First();
        var monMoi = store.ThemMonAn(new MonAn
        {
            Ten = "Chả giò", NhomMonId = nhomKhaiVi.Id, GiaBanVnd = 55000, DonViTinh = "Phần",
            MoTaNgan = "  Giòn rụm  ", ThoiGianCheBienPhut = 10
        });
        var monMoiHienThi = store.LayThucDonCongKhai()[0].MonAn.Single(m => m.Id == monMoi.Id);
        check(monMoiHienThi.AnhUrl == AnhMonAn.AnhMacDinh && monMoiHienThi.MoTaNgan == "Giòn rụm", "Public menu: new dish without image uses default; description trimmed");
        check(store.LayThucDonCongKhai()[0].MonAn.Select(m => m.Id).SequenceEqual(new[] { goiCuon.Id, monMoi.Id }), "Public menu: dishes in stable order within a group");

        monMoi.TrangThai = TrangThaiMon.NgungBan;
        check(store.LayThucDonCongKhai().SelectMany(n => n.MonAn).All(m => m.Id != monMoi.Id), "Public menu: stopped dish hidden");

        var nhomAn = store.LayTatCaNhomMon().Last();
        store.DatTrangThaiNhomMon(nhomAn.Id, false);
        var sauKhiAn = store.LayThucDonCongKhai();
        check(sauKhiAn.All(n => n.Id != nhomAn.Id) && sauKhiAn.Count == 4, "Public menu: inactive group hidden with its dishes");

        var nhomRong = store.AddNhomMon(new NhomMon { Ten = "Món theo mùa", ThuTuHienThi = 10 });
        check(store.LayThucDonCongKhai().Any(n => n.Id == nhomRong.Id && n.MonAn.Count == 0), "Public menu: active empty group still listed");

        var expectedIds = store.LayThucDonCongKhai().Select(n => n.Id).ToArray();
        var viewModel = (new ThucDonController(store).Index() as ViewResult)?.Model as IReadOnlyList<NhomThucDonCongKhai>;
        check(viewModel is not null && viewModel.Select(n => n.Id).SequenceEqual(expectedIds), "Public menu: ThucDonController.Index returns public menu view");
        var apiModel = (new ThucDonApiController(store).DanhSach().Result as OkObjectResult)?.Value as IReadOnlyList<NhomThucDonCongKhai>;
        check(apiModel is not null && apiModel.Select(n => n.Id).SequenceEqual(expectedIds), "Public menu: API controller returns the same groups");
        check(typeof(ThucDonController).IsDefined(typeof(AllowAnonymousAttribute), inherit: true)
            && typeof(ThucDonApiController).IsDefined(typeof(AllowAnonymousAttribute), inherit: true),
            "Public menu: /ThucDon and /api/thuc-don allow anonymous access");
        check(!typeof(RestaurantManagement.Web.Pages.GoiMon.IndexModel).IsDefined(typeof(AllowAnonymousAttribute), inherit: true),
            "Public menu: staff ordering page /GoiMon still requires login");

        var tho = new[]
        {
            new MonDangBanTho(2, 1, "B", null, 10000, "Ly", null, 2, true),
            new MonDangBanTho(1, 1, "A", "x", 20000, "Ly", "/a.png", 2, false),
            new MonDangBanTho(3, 1, "C", "y", 30000, "Ly", null, 1, false),
            new MonDangBanTho(4, 9, "Mồ côi", "z", 30000, "Ly", null, 1, false)
        };
        var built = ThucDonCongKhaiBuilder.Tao(new[] { (1, "Nhóm 1"), (5, "Nhóm trống") }, tho);
        check(built.Select(n => n.Id).SequenceEqual(new[] { 1, 5 }) && built[0].MonAn.Select(m => m.Id).SequenceEqual(new[] { 3, 1, 2 }) && built[1].MonAn.Count == 0,
            "Public menu: builder orders by sort order then Id and drops dishes of hidden groups");
        check(built[0].MonAn.Single(m => m.Id == 2) is { HetTrongNgay: true, MoTaNgan: "" }, "Public menu: sold-out-today flag and empty description carried");
    }
}
