using System.ComponentModel.DataAnnotations;
using RestaurantManagement.Data.Models;
using RestaurantManagement.Web.Services;
using static RestaurantManagement.Web.Services.CategoryNameRules;

internal static class CategoryTests
{
    internal static void Run(Action<bool, string> check)
    {
        var deletion = new InMemoryMenuStore();
        var blocked = false;
        try { deletion.DeleteCategory(3); } catch (ValidationException) { blocked = true; }
        check(blocked && deletion.GetCategory(3) != null, "Delete: populated group rejected");
        var soup = deletion.GetDishesByCategory(3).Single();
        soup.Status = DishStatus.Discontinued;
        blocked = false;
        try { deletion.DeleteCategory(3); } catch (ValidationException) { blocked = true; }
        check(blocked, "Delete: stopped dishes still block deletion");
        deletion.UpdateDish(new Dish { Id = soup.Id, Name = soup.Name, CategoryId = 2, PriceVnd = soup.PriceVnd, Unit = soup.Unit, PrepMinutes = soup.PrepMinutes, Status = soup.Status });
        check(deletion.DeleteCategory(3) && deletion.GetCategory(3) == null, "Delete: group removable after moving all dishes");
        check(deletion.GetDish(soup.Id)?.CategoryId == 2 && deletion.GetAllDishes().Count() == 5, "Delete: moved dish remains intact");
        check(deletion.GetAllCategories().Select(n => n.SortOrder).SequenceEqual(Enumerable.Range(1,4)), "Delete: remaining positions contiguous");
        var disposable = deletion.AddCategory(new DishCategory { Name = "Nhóm rỗng" });
        check(deletion.DeleteCategory(disposable.Id), "Delete: empty active group removable");
        check(!deletion.DeleteCategory(disposable.Id), "Delete: already deleted ID returns false");
        var raceSafe = true;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var raceStore = new InMemoryMenuStore();
            var target = raceStore.AddCategory(new DishCategory { Name = "Đồng thời" });
            Parallel.Invoke(
                () => { try { raceStore.DeleteCategory(target.Id); } catch (ValidationException) { } },
                () => { try { raceStore.AddDish(new Dish { Name = "Món mới", CategoryId = target.Id }); } catch (ArgumentException) { } });
            raceSafe &= raceStore.GetAllDishes().All(m => raceStore.GetCategory(m.CategoryId) != null);
        }
        check(raceSafe, "Delete: concurrent add/delete never leaves orphan dishes");
        var lifecycle = new InMemoryMenuStore();
        var emptyGroup = lifecycle.AddCategory(new DishCategory { Name = "Món nướng", SortOrder = 6 });
        check(lifecycle.SetCategoryActive(emptyGroup.Id, false) && lifecycle.GetCategory(emptyGroup.Id)?.IsActive == false, "Lifecycle: stop empty group without deleting it");
        var dishes = lifecycle.GetDishesByCategory(5).ToArray();
        var before = lifecycle.GetCategory(5)!;
        check(lifecycle.SetCategoryActive(5, false), "Lifecycle: stop populated group");
        check(!lifecycle.GetMenuByCategory().Any(n => n.Id == 5), "Lifecycle: stopped group hidden from menu");
        check(lifecycle.GetDishesByCategory(5).SequenceEqual(dishes) && dishes.All(m => m.Status == DishStatus.OnSale), "Lifecycle: dishes and their sale status preserved");
        check(lifecycle.GetCategory(5) is { Name: "Đồ uống" } savedGroup && savedGroup.SortOrder == before.SortOrder, "Lifecycle: name and position preserved");
        var checkout = new RestaurantManagement.Web.Pages.Ordering.CheckoutModel(lifecycle) { CartJson = "[{\"dishId\":5,\"quantity\":1}]" };
        check(checkout.OnPost() is Microsoft.AspNetCore.Mvc.BadRequestObjectResult && !lifecycle.GetAllOrders().Any(), "Lifecycle: stale cart rejected without creating order");
        check(lifecycle.SetCategoryActive(5, false), "Lifecycle: repeated stop is idempotent");
        check(lifecycle.SetCategoryActive(5, true) && lifecycle.GetMenuByCategory().Single(n => n.Id == 5).Dishes.Count == dishes.Length, "Lifecycle: reactivate restores dishes");
        check(!lifecycle.SetCategoryActive(int.MaxValue, false), "Lifecycle: unknown group rejected");
        var orderedStore = new InMemoryMenuStore();
        var original = orderedStore.GetAllCategories().Select(n => n.Id).ToArray();
        var drinksFirst = new[] { original[4], original[0], original[1], original[2], original[3] };
        orderedStore.SaveCategoryOrder(drinksFirst, original);
        check(orderedStore.GetAllCategories().First().Name == "Đồ uống", "Order: move drinks to first position");
        check(orderedStore.GetAllCategories().Select(n => n.SortOrder).SequenceEqual(Enumerable.Range(1,5)), "Order: positions are contiguous and unique");
        check(orderedStore.GetMenuByCategory().Select(n => n.Id).SequenceEqual(drinksFirst), "Order: menu reflects saved order");
        foreach (var bad in new[] { original.Take(4).ToArray(), new[] {1,1,2,3,4}, new[] {1,2,3,4,999} })
        {
            var failed = false;
            try { orderedStore.SaveCategoryOrder(bad, drinksFirst); }
            catch (ValidationException) { failed = true; }
            check(failed && orderedStore.GetAllCategories().Select(n => n.Id).SequenceEqual(drinksFirst), "Order: invalid permutation rejected without partial writes");
        }
        var stale = false;
        try { orderedStore.SaveCategoryOrder(original, original); }
        catch (ValidationException) { stale = true; }
        check(stale, "Order: stale order rejected");
        orderedStore.SaveCategoryOrder(original, drinksFirst);
        check(orderedStore.GetAllCategories().Last().Name == "Đồ uống", "Order: move drinks to last position");
        var swapped = new[] { original[1], original[0], original[2], original[3], original[4] };
        orderedStore.SaveCategoryOrder(swapped, original);
        check(orderedStore.GetAllCategories().Select(n => n.Id).SequenceEqual(swapped), "Order: swap two groups");
        orderedStore.AddCategory(new DishCategory { Name = "Nhóm mới" });
        stale = false;
        try { orderedStore.SaveCategoryOrder(original, swapped); }
        catch (ValidationException) { stale = true; }
        check(stale, "Order: added category makes old form stale");
        var store = new InMemoryMenuStore();
        bool Reject(Action action, string error)
        {
            try { action(); return false; }
            catch (ValidationException ex) { return ex.Message == error; }
        }
        var group = store.AddCategory(new DishCategory { Name = "  Món   nướng  ", SortOrder = 6 });
        check(group.Name == "Món nướng", "Category: create normalizes whitespace");
        check(Reject(() => store.AddCategory(new DishCategory { Name = "MÓN NƯỚNG" }), CategoryNameRules.DuplicateName), "Category: duplicate ignores case");
        check(Reject(() => store.AddCategory(new DishCategory { Name = " Món\t nướng\n" }), CategoryNameRules.DuplicateName), "Category: duplicate ignores repeated whitespace");
        check(Reject(() => store.AddCategory(new DishCategory { Name = group.Name.Normalize(System.Text.NormalizationForm.FormD) }), CategoryNameRules.DuplicateName), "Category: Unicode composed and decomposed names are duplicates");
        check(Reject(() => store.AddCategory(new DishCategory { Name = " \t " }), CategoryNameRules.EmptyName), "Category: blank rejected");
        check(Reject(() => store.AddCategory(new DishCategory { Name = new string('a', 51) }), CategoryNameRules.NameTooLong), "Category: 51 characters rejected");
        check(store.AddCategory(new DishCategory { Name = new string('a', 50) }).Name.Length == 50, "Category: 50 characters accepted");
        check(store.AddCategory(new DishCategory { Name = "Mon nuong" }).Id != group.Id, "Category: Vietnamese accents distinguish names");
        check(Reject(() => store.RenameCategory(group.Id, "KHAI VỊ"), CategoryNameRules.DuplicateName), "Category: rename to existing name rejected");
        check(Reject(() => store.RenameCategory(group.Id, " "), CategoryNameRules.EmptyName), "Category: blank rename rejected");
        check(Reject(() => store.RenameCategory(group.Id, new string('x', 51)), CategoryNameRules.NameTooLong), "Category: long rename rejected");
        check(store.RenameCategory(group.Id, "Món nướng BBQ"), "Category: valid rename succeeds");
        check(store.RenameCategory(group.Id, " MÓN NƯỚNG BBQ "), "Category: own name excluded from duplicate check");
        var saved = store.GetCategory(group.Id)!;
        check(saved.SortOrder == 6 && saved.IsActive && saved.Name == "MÓN NƯỚNG BBQ", "Category: rename preserves ID, status and display order");
        check(!store.RenameCategory(int.MaxValue, "Không tồn tại"), "Category: unknown ID rejected");
        store.AddCategory(new DishCategory { Name = "Nhóm ẩn", IsActive = false });
        check(Reject(() => store.AddCategory(new DishCategory { Name = "nhóm ẩn" }), CategoryNameRules.DuplicateName), "Category: inactive names also reserved");
        var successes = 0;
        Parallel.For(0, 20, _ =>
        {
            try { store.AddCategory(new DishCategory { Name = "Đồng thời" }); Interlocked.Increment(ref successes); }
            catch (ValidationException) { }
        });
        check(successes == 1, "Category: concurrent duplicate requests create only one group");
    }
}
