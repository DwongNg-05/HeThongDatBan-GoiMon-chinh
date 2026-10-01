using RestaurantManagement.Data.Models;
using RestaurantManagement.Web.Services;

internal static class MenuTests
{
    internal static void Run(Action<bool, string> check)
    {
        var store = new InMemoryQuanLyMonStore();
        string[] expected = ["Khai vị", "Món chính", "Lẩu", "Tráng miệng", "Đồ uống"];
        var menu = store.LayThucDonTheoNhom();
        check(menu.Select(n => n.Ten).SequenceEqual(expected), "Menu: exactly five default groups in required order");
        check(menu.All(n => n.MonAn.Count > 0 && n.MonAn.All(m => m.NhomMonId == n.Id)), "Menu: sample dishes belong to their displayed group");
        check(menu.All(n => store.LayMonAnTheoNhom(n.Id).All(m => m.NhomMonId == n.Id)), "Menu: query dishes by group");
        var publicMenu = new RestaurantManagement.Web.Controllers.ThucDonController(store);
        int[] PublicIds() => ((IReadOnlyList<NhomThucDonCongKhai>)((Microsoft.AspNetCore.Mvc.ViewResult)publicMenu.Index()).Model!).Select(n => n.Id).ToArray();
        var orderPage = new RestaurantManagement.Web.Pages.GoiMon.IndexModel(store);
        orderPage.OnGet();
        check(PublicIds().SequenceEqual(orderPage.NhomMon.Select(n => n.Id)), "Menu: public and ordering pages have identical group order");
        var first = store.LayTatCaNhomMon().First();
        first.ThuTuHienThi = 100;
        check(store.LayThucDonTheoNhom().Last().Id == first.Id, "Menu: explicit display order takes precedence over ID");
        foreach (var dish in store.LayMonAnTheoNhom(first.Id)) dish.TrangThai = TrangThaiMon.NgungBan;
        check(store.LayThucDonTheoNhom().Single(n => n.Id == first.Id).MonAn.Count == 0, "Menu: group with no available dishes remains visible");
        var empty = store.AddNhomMon(new NhomMon { Ten = "Nhóm rỗng", ThuTuHienThi = 6 });
        check(store.LayThucDonTheoNhom().Any(n => n.Id == empty.Id && n.MonAn.Count == 0), "Menu: group with no dishes remains visible");
        var inactive = store.LayTatCaNhomMon().First();
        inactive.DangSuDung = false;
        check(store.LayThucDonTheoNhom().All(n => n.Id != inactive.Id), "Menu: inactive group and its dishes are excluded");
        orderPage.OnGet();
        check(PublicIds().SequenceEqual(orderPage.NhomMon.Select(n => n.Id)), "Menu: both pages remain consistent after status/order changes");
        var invalidRejected = false;
        try { store.ThemMonAn(new MonAn { NhomMonId = int.MaxValue }); }
        catch (ArgumentException) { invalidRejected = true; }
        check(invalidRejected, "Menu: dish cannot reference a missing group");
        var dishToEdit = store.LayTatCaMonAn().First();
        invalidRejected = false;
        try { store.CapNhatMonAn(new MonAn { Id = dishToEdit.Id, NhomMonId = int.MaxValue }); }
        catch (ArgumentException) { invalidRejected = true; }
        check(invalidRejected, "Menu: edit cannot move a dish to a missing group");
        foreach (var group in store.LayTatCaNhomMon()) group.DangSuDung = false;
        check(store.LayThucDonTheoNhom().Count == 0, "Menu: no active groups returns an empty menu");
    }
}
