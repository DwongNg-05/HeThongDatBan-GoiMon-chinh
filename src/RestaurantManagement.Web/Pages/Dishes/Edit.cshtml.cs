using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Services;
using RestaurantManagement.Data.Models;

namespace RestaurantManagement.Web.Pages.Dishes;

[RequestFormLimits(MultipartBodyLengthLimit = DishImageRules.MaxBytes + 1024 * 1024)]
[RequestSizeLimit(DishImageRules.MaxBytes + 1024 * 1024)]
public class EditModel : PageModel
{
    private readonly IMenuStore _store;
    private readonly IDishImageStorage _imageStorage;

    public EditModel(IMenuStore store, IDishImageStorage imageStorage)
    {
        _store = store;
        _imageStorage = imageStorage;
    }

    [BindProperty]
    public Dish Dish { get; set; } = new Dish();

    /// <summary>Ảnh mới (JPG/PNG, tối đa 5 MB). Bỏ trống thì giữ ảnh hiện tại.</summary>
    [BindProperty]
    public IFormFile? ImageFile { get; set; }

    /// <summary>Ảnh đang hiển thị của món (ảnh riêng hoặc ảnh mặc định).</summary>
    public string CurrentImage { get; private set; } = DishImage.DefaultImage;

    public IEnumerable<DishCategory> Categories { get; set; } = Enumerable.Empty<DishCategory>();

    public IActionResult OnGet(int id)
    {
        Categories = _store.GetAllCategories();
        var dish = _store.GetDish(id);
        if (dish == null) return NotFound();
        Dish = new Dish
        {
            Id = dish.Id,
            Name = dish.Name,
            CategoryId = dish.CategoryId,
            PriceVnd = dish.PriceVnd,
            Unit = dish.Unit,
            ShortDescription = dish.ShortDescription,
            PrepMinutes = dish.PrepMinutes,
            Status = dish.Status,
            ImagePath = dish.ImagePath
        };
        var categoryDefault = Categories.FirstOrDefault(n => n.Id == dish.CategoryId);
        CurrentImage = DishImage.Resolve(dish.ImagePath,
            categoryDefault?.DefaultImagePath ?? DishImage.DefaultForCategory(categoryDefault?.Name));
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Categories = _store.GetAllCategories();
        ModelState.Remove("Dish.ImagePath");
        var existing = _store.GetDish(Dish.Id);
        if (existing == null) return NotFound();
        // Ảnh cũ lấy từ dữ liệu đã lưu, không nhận đường dẫn ảnh từ biểu mẫu.
        var oldImage = existing.ImagePath;
        var categoryForSelection = Categories.FirstOrDefault(n => n.Id == Dish.CategoryId);
        CurrentImage = DishImage.Resolve(oldImage,
            categoryForSelection?.DefaultImagePath ?? DishImage.DefaultForCategory(categoryForSelection?.Name));

        // Reject unauthenticated saves before the store throws an exception.
        var currentUser = HttpContext.RequestServices.GetRequiredService<RestaurantManagement.Web.Security.ICurrentUser>();
        if (!currentUser.IsAuthenticated)
        {
            ModelState.AddModelError(string.Empty, "Chưa đăng nhập nên không thể lưu thay đổi. Vui lòng đăng nhập bằng tài khoản có quyền sửa món.");
            return Page();
        }

        if (!Categories.Any(n => n.Id == Dish.CategoryId))
            ModelState.AddModelError("Dish.CategoryId", "Nhóm món không tồn tại.");

        if (ImageFile is not null)
        {
            var result = DishImageRules.Validate(ImageFile);
            if (!result.IsValid) ModelState.AddModelError(nameof(ImageFile), result.Error!);
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        string? newImage = null;
        try
        {
            if (ImageFile is not null)
                newImage = await _imageStorage.SaveAsync(ImageFile, cancellationToken);
            Dish.ImagePath = newImage ?? oldImage;   // đường dẫn ảnh ghi vào MenuItems.ImagePath
            _store.UpdateDish(Dish);
        }
        catch (ValidationException ex)
        {
            _imageStorage.Delete(newImage);
            ModelState.AddModelError(nameof(ImageFile), ex.Message);
            return Page();
        }
        catch (UnauthorizedAccessException)
        {
            _imageStorage.Delete(newImage);
            ModelState.AddModelError(string.Empty, "Phiên đăng nhập không còn hợp lệ. Vui lòng đăng nhập lại trước khi lưu.");
            return Page();
        }
        catch (ArgumentException)
        {
            _imageStorage.Delete(newImage);
            ModelState.AddModelError("Dish.CategoryId", "Nhóm món không còn tồn tại. Vui lòng chọn nhóm khác.");
            Categories = _store.GetAllCategories();
            return Page();
        }
        catch
        {
            _imageStorage.Delete(newImage);
            throw;
        }

        // Đã lưu ảnh mới thành công: dọn ảnh tải lên cũ (ảnh mẫu trong /images không bị xoá).
        if (newImage is not null && oldImage != newImage)
            _imageStorage.Delete(oldImage);

        return RedirectToPage("/Dishes/Index");
    }
}
