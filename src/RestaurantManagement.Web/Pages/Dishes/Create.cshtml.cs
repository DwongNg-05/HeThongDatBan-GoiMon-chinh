using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Services;
using RestaurantManagement.Data.Models;

namespace RestaurantManagement.Web.Pages.Dishes;

[RequestFormLimits(MultipartBodyLengthLimit = DishImageRules.MaxBytes + 1024 * 1024)]
[RequestSizeLimit(DishImageRules.MaxBytes + 1024 * 1024)]
public class CreateModel : PageModel
{
    private readonly IMenuStore _store;
    private readonly IDishImageStorage _imageStorage;

    public CreateModel(IMenuStore store, IDishImageStorage imageStorage)
    {
        _store = store;
        _imageStorage = imageStorage;
    }

    [BindProperty]
    public Dish Dish { get; set; } = new Dish();

    /// <summary>Ảnh món (JPG/PNG, tối đa 5 MB). Không bắt buộc: bỏ trống thì dùng ảnh mặc định của nhóm.</summary>
    [BindProperty]
    public IFormFile? ImageFile { get; set; }

    public IEnumerable<DishCategory> Categories { get; set; } = Enumerable.Empty<DishCategory>();

    public void OnGet()
    {
        Categories = _store.GetAllCategories();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Categories = _store.GetAllCategories();

        // Đường dẫn ảnh chỉ do máy chủ tạo ra, không nhận từ biểu mẫu.
        Dish.ImagePath = null;
        ModelState.Remove("Dish.ImagePath");

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
            Dish.ImagePath = newImage;   // ghi đường dẫn ảnh vào MenuItems.ImagePath
            _store.AddDish(Dish);
        }
        catch (ValidationException ex)
        {
            _imageStorage.Delete(newImage);
            ModelState.AddModelError(nameof(ImageFile), ex.Message);
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
        return RedirectToPage("/Dishes/Index");
    }
}
