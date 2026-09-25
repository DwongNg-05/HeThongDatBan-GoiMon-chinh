using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using RestaurantManagement.Data;
using RestaurantManagement.Web.ViewModels;
using System.Security.Cryptography;

namespace RestaurantManagement.Web.Controllers;

[Route("admin/employee-accounts")]
public sealed class EmployeeAccountsController(IEmployeeAccountStore employeeAccounts) : Controller
{
    private const string PasswordAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        return View(await employeeAccounts.GetEmployeeAccountsAsync(cancellationToken));
    }

    [HttpGet("create")]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var model = new CreateEmployeeAccountViewModel();
        await PopulateRolesAsync(model, cancellationToken);
        return View(model);
    }

    [HttpGet("check-username")]
    [HttpPost("check-username")]
    public async Task<IActionResult> CheckUserName(string userName, CancellationToken cancellationToken)
    {
        var value = userName?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return Json(true);
        }

        return Json(await employeeAccounts.UserNameExistsAsync(value, cancellationToken)
            ? "Tên đăng nhập này đã được sử dụng."
            : true);
    }

    [HttpGet("check-phone-number")]
    [HttpPost("check-phone-number")]
    public async Task<IActionResult> CheckPhoneNumber(string phoneNumber, CancellationToken cancellationToken)
    {
        var value = phoneNumber?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return Json(true);
        }

        return Json(await employeeAccounts.PhoneNumberExistsAsync(value, cancellationToken)
            ? "Số điện thoại này đã được sử dụng."
            : true);
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateEmployeeAccountViewModel model, CancellationToken cancellationToken)
    {
        model.FullName = model.FullName?.Trim() ?? string.Empty;
        model.PhoneNumber = model.PhoneNumber?.Trim() ?? string.Empty;
        model.UserName = model.UserName?.Trim() ?? string.Empty;

        if (!model.RoleId.HasValue || !await employeeAccounts.IsInternalRoleAsync(model.RoleId.Value, cancellationToken))
        {
            ModelState.AddModelError(nameof(model.RoleId), "Chọn một vai trò nội bộ hợp lệ.");
        }

        if (!string.IsNullOrWhiteSpace(model.PhoneNumber) &&
            await employeeAccounts.PhoneNumberExistsAsync(model.PhoneNumber, cancellationToken))
        {
            ModelState.AddModelError(nameof(model.PhoneNumber), "Số điện thoại này đã được sử dụng.");
        }

        if (!string.IsNullOrWhiteSpace(model.UserName) &&
            await employeeAccounts.UserNameExistsAsync(model.UserName, cancellationToken))
        {
            ModelState.AddModelError(nameof(model.UserName), "Tên đăng nhập này đã được sử dụng.");
        }

        if (!ModelState.IsValid)
        {
            await PopulateRolesAsync(model, cancellationToken);
            return View(model);
        }

        var temporaryPassword = GenerateTemporaryPassword();
        try
        {
            await employeeAccounts.CreateAsync(
                new NewEmployeeAccount(
                    model.RoleId!.Value,
                    model.FullName,
                    model.PhoneNumber,
                    model.UserName,
                    BCrypt.Net.BCrypt.HashPassword(temporaryPassword, workFactor: 12)),
                cancellationToken);
        }
        catch (DuplicateEmployeeAccountException)
        {
            if (await employeeAccounts.PhoneNumberExistsAsync(model.PhoneNumber, cancellationToken))
            {
                ModelState.AddModelError(nameof(model.PhoneNumber), "Số điện thoại này đã được sử dụng.");
            }

            if (await employeeAccounts.UserNameExistsAsync(model.UserName, cancellationToken))
            {
                ModelState.AddModelError(nameof(model.UserName), "Tên đăng nhập này đã được sử dụng.");
            }

            if (ModelState.ErrorCount == 0)
            {
                ModelState.AddModelError(string.Empty, "Không thể tạo tài khoản. Vui lòng thử lại.");
            }

            await PopulateRolesAsync(model, cancellationToken);
            return View(model);
        }

        TempData["TemporaryPassword"] = temporaryPassword;
        TempData["SuccessMessage"] = $"Đã tạo tài khoản cho {model.FullName}.";
        return RedirectToAction(nameof(Create));
    }

    [HttpPost("{accountId:int}/deactivate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(int accountId, CancellationToken cancellationToken)
    {
        if (await employeeAccounts.DeactivateAsync(accountId, cancellationToken))
        {
            TempData["StatusMessage"] = "Đã đặt tài khoản sang trạng thái đã nghỉ. Tài khoản không thể đăng nhập.";
        }
        else
        {
            TempData["StatusMessage"] = "Tài khoản này đã ở trạng thái đã nghỉ hoặc không tồn tại.";
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateRolesAsync(CreateEmployeeAccountViewModel model, CancellationToken cancellationToken)
    {
        model.Roles = (await employeeAccounts.GetInternalRolesAsync(cancellationToken))
            .Select(role => new SelectListItem(role.Name, role.Id.ToString()))
            .ToList();
    }

    private static string GenerateTemporaryPassword()
    {
        var characters = new char[8];
        for (var index = 0; index < characters.Length; index++)
        {
            characters[index] = PasswordAlphabet[RandomNumberGenerator.GetInt32(PasswordAlphabet.Length)];
        }

        return new string(characters);
    }
}
