using System.ComponentModel.DataAnnotations;
using RestaurantManagement.Data.Models;
using RestaurantManagement.Web.Services;
using static RestaurantManagement.Web.Services.QuyTacTenNhomMon;

internal static class CategoryTests
{
    internal static void Run(Action<bool, string> check)
    {
        var deletion = new InMemoryQuanLyMonStore();
        var blocked = false;
        try { deletion.XoaNhomMon(3); } catch (ValidationException) { blocked = true; }
        check(blocked && deletion.LayNhomMon(3) != null, "Delete: populated group rejected");
        var soup = deletion.LayMonAnTheoNhom(3).Single();
        soup.TrangThai = TrangThaiMon.NgungBan;
        blocked = false;
        try { deletion.XoaNhomMon(3); } catch (ValidationException) { blocked = true; }
        check(blocked, "Delete: stopped dishes still block deletion");
        deletion.CapNhatMonAn(new MonAn { Id = soup.Id, Ten = soup.Ten, NhomMonId = 2, GiaBanVnd = soup.GiaBanVnd, DonViTinh = soup.DonViTinh, ThoiGianCheBienPhut = soup.ThoiGianCheBienPhut, TrangThai = soup.TrangThai });
        check(deletion.XoaNhomMon(3) && deletion.LayNhomMon(3) == null, "Delete: group removable after moving all dishes");
        check(deletion.LayMonAn(soup.Id)?.NhomMonId == 2 && deletion.LayTatCaMonAn().Count() == 5, "Delete: moved dish remains intact");
        check(deletion.LayTatCaNhomMon().Select(n => n.ThuTuHienThi).SequenceEqual(Enumerable.Range(1,4)), "Delete: remaining positions contiguous");
        var disposable = deletion.AddNhomMon(new NhomMon { Ten = "Nhóm rỗng" });
        check(deletion.XoaNhomMon(disposable.Id), "Delete: empty active group removable");
        check(!deletion.XoaNhomMon(disposable.Id), "Delete: already deleted ID returns false");
        var raceSafe = true;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var raceStore = new InMemoryQuanLyMonStore();
            var target = raceStore.AddNhomMon(new NhomMon { Ten = "Đồng thời" });
            Parallel.Invoke(
                () => { try { raceStore.XoaNhomMon(target.Id); } catch (ValidationException) { } },
                () => { try { raceStore.ThemMonAn(new MonAn { Ten = "Món mới", NhomMonId = target.Id }); } catch (ArgumentException) { } });
            raceSafe &= raceStore.LayTatCaMonAn().All(m => raceStore.LayNhomMon(m.NhomMonId) != null);
        }
        check(raceSafe, "Delete: concurrent add/delete never leaves orphan dishes");
        var lifecycle = new InMemoryQuanLyMonStore();
        var emptyGroup = lifecycle.AddNhomMon(new NhomMon { Ten = "Món nướng", ThuTuHienThi = 6 });
        check(lifecycle.DatTrangThaiNhomMon(emptyGroup.Id, false) && lifecycle.LayNhomMon(emptyGroup.Id)?.DangSuDung == false, "Lifecycle: stop empty group without deleting it");
        var dishes = lifecycle.LayMonAnTheoNhom(5).ToArray();
        var before = lifecycle.LayNhomMon(5)!;
        check(lifecycle.DatTrangThaiNhomMon(5, false), "Lifecycle: stop populated group");
        check(!lifecycle.LayThucDonTheoNhom().Any(n => n.Id == 5), "Lifecycle: stopped group hidden from menu");
        check(lifecycle.LayMonAnTheoNhom(5).SequenceEqual(dishes) && dishes.All(m => m.TrangThai == TrangThaiMon.DangBan), "Lifecycle: dishes and their sale status preserved");
        check(lifecycle.LayNhomMon(5) is { Ten: "Đồ uống" } savedGroup && savedGroup.ThuTuHienThi == before.ThuTuHienThi, "Lifecycle: name and position preserved");
        var checkout = new RestaurantManagement.Web.Pages.GoiMon.CheckoutModel(lifecycle) { CartJson = "[{\"monId\":5,\"soLuong\":1}]" };
        check(checkout.OnPost() is Microsoft.AspNetCore.Mvc.BadRequestObjectResult && !lifecycle.LayTatCaDonHang().Any(), "Lifecycle: stale cart rejected without creating order");
        check(lifecycle.DatTrangThaiNhomMon(5, false), "Lifecycle: repeated stop is idempotent");
        check(lifecycle.DatTrangThaiNhomMon(5, true) && lifecycle.LayThucDonTheoNhom().Single(n => n.Id == 5).MonAn.Count == dishes.Length, "Lifecycle: reactivate restores dishes");
        check(!lifecycle.DatTrangThaiNhomMon(int.MaxValue, false), "Lifecycle: unknown group rejected");
        var orderedStore = new InMemoryQuanLyMonStore();
        var original = orderedStore.LayTatCaNhomMon().Select(n => n.Id).ToArray();
        var drinksFirst = new[] { original[4], original[0], original[1], original[2], original[3] };
        orderedStore.LuuThuTuNhomMon(drinksFirst, original);
        check(orderedStore.LayTatCaNhomMon().First().Ten == "Đồ uống", "Order: move drinks to first position");
        check(orderedStore.LayTatCaNhomMon().Select(n => n.ThuTuHienThi).SequenceEqual(Enumerable.Range(1,5)), "Order: positions are contiguous and unique");
        check(orderedStore.LayThucDonTheoNhom().Select(n => n.Id).SequenceEqual(drinksFirst), "Order: menu reflects saved order");
        foreach (var bad in new[] { original.Take(4).ToArray(), new[] {1,1,2,3,4}, new[] {1,2,3,4,999} })
        {
            var failed = false;
            try { orderedStore.LuuThuTuNhomMon(bad, drinksFirst); }
            catch (ValidationException) { failed = true; }
            check(failed && orderedStore.LayTatCaNhomMon().Select(n => n.Id).SequenceEqual(drinksFirst), "Order: invalid permutation rejected without partial writes");
        }
        var stale = false;
        try { orderedStore.LuuThuTuNhomMon(original, original); }
        catch (ValidationException) { stale = true; }
        check(stale, "Order: stale order rejected");
        orderedStore.LuuThuTuNhomMon(original, drinksFirst);
        check(orderedStore.LayTatCaNhomMon().Last().Ten == "Đồ uống", "Order: move drinks to last position");
        var swapped = new[] { original[1], original[0], original[2], original[3], original[4] };
        orderedStore.LuuThuTuNhomMon(swapped, original);
        check(orderedStore.LayTatCaNhomMon().Select(n => n.Id).SequenceEqual(swapped), "Order: swap two groups");
        orderedStore.AddNhomMon(new NhomMon { Ten = "Nhóm mới" });
        stale = false;
        try { orderedStore.LuuThuTuNhomMon(original, swapped); }
        catch (ValidationException) { stale = true; }
        check(stale, "Order: added category makes old form stale");
        var store = new InMemoryQuanLyMonStore();
        bool Reject(Action action, string error)
        {
            try { action(); return false; }
            catch (ValidationException ex) { return ex.Message == error; }
        }
        var group = store.AddNhomMon(new NhomMon { Ten = "  Món   nướng  ", ThuTuHienThi = 6 });
        check(group.Ten == "Món nướng", "Category: create normalizes whitespace");
        check(Reject(() => store.AddNhomMon(new NhomMon { Ten = "MÓN NƯỚNG" }), QuyTacTenNhomMon.TenTrung), "Category: duplicate ignores case");
        check(Reject(() => store.AddNhomMon(new NhomMon { Ten = " Món\t nướng\n" }), QuyTacTenNhomMon.TenTrung), "Category: duplicate ignores repeated whitespace");
        check(Reject(() => store.AddNhomMon(new NhomMon { Ten = group.Ten.Normalize(System.Text.NormalizationForm.FormD) }), QuyTacTenNhomMon.TenTrung), "Category: Unicode composed and decomposed names are duplicates");
        check(Reject(() => store.AddNhomMon(new NhomMon { Ten = " \t " }), QuyTacTenNhomMon.TenRong), "Category: blank rejected");
        check(Reject(() => store.AddNhomMon(new NhomMon { Ten = new string('a', 51) }), QuyTacTenNhomMon.TenQuaDai), "Category: 51 characters rejected");
        check(store.AddNhomMon(new NhomMon { Ten = new string('a', 50) }).Ten.Length == 50, "Category: 50 characters accepted");
        check(store.AddNhomMon(new NhomMon { Ten = "Mon nuong" }).Id != group.Id, "Category: Vietnamese accents distinguish names");
        check(Reject(() => store.SuaTenNhomMon(group.Id, "KHAI VỊ"), QuyTacTenNhomMon.TenTrung), "Category: rename to existing name rejected");
        check(Reject(() => store.SuaTenNhomMon(group.Id, " "), QuyTacTenNhomMon.TenRong), "Category: blank rename rejected");
        check(Reject(() => store.SuaTenNhomMon(group.Id, new string('x', 51)), QuyTacTenNhomMon.TenQuaDai), "Category: long rename rejected");
        check(store.SuaTenNhomMon(group.Id, "Món nướng BBQ"), "Category: valid rename succeeds");
        check(store.SuaTenNhomMon(group.Id, " MÓN NƯỚNG BBQ "), "Category: own name excluded from duplicate check");
        var saved = store.LayNhomMon(group.Id)!;
        check(saved.ThuTuHienThi == 6 && saved.DangSuDung && saved.Ten == "MÓN NƯỚNG BBQ", "Category: rename preserves ID, status and display order");
        check(!store.SuaTenNhomMon(int.MaxValue, "Không tồn tại"), "Category: unknown ID rejected");
        store.AddNhomMon(new NhomMon { Ten = "Nhóm ẩn", DangSuDung = false });
        check(Reject(() => store.AddNhomMon(new NhomMon { Ten = "nhóm ẩn" }), QuyTacTenNhomMon.TenTrung), "Category: inactive names also reserved");
        var successes = 0;
        Parallel.For(0, 20, _ =>
        {
            try { store.AddNhomMon(new NhomMon { Ten = "Đồng thời" }); Interlocked.Increment(ref successes); }
            catch (ValidationException) { }
        });
        check(successes == 1, "Category: concurrent duplicate requests create only one group");
    }
}
