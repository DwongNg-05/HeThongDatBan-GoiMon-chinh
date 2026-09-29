namespace RestaurantManagement.Data;

public interface IEmployeeAccountStore
{
    Task<IReadOnlyList<EmployeeRole>> GetInternalRolesAsync(CancellationToken cancellationToken = default);
    Task<bool> IsInternalRoleAsync(int roleId, CancellationToken cancellationToken = default);
    Task<bool> PhoneNumberExistsAsync(string phoneNumber, CancellationToken cancellationToken = default);
    Task<bool> UserNameExistsAsync(string userName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EmployeeAccountListItem>> GetEmployeeAccountsAsync(CancellationToken cancellationToken = default);
    Task<bool> DeactivateAsync(int accountId, CancellationToken cancellationToken = default);
    Task<bool> IsLoginAllowedAsync(string userNameOrPhone, CancellationToken cancellationToken = default);
    Task CreateAsync(NewEmployeeAccount account, CancellationToken cancellationToken = default);
}

public sealed record EmployeeRole(int Id, string Name);

public sealed record EmployeeAccountListItem(
    int Id,
    string FullName,
    string PhoneNumber,
    string UserName,
    string RoleName,
    bool IsActive);

public sealed record NewEmployeeAccount(
    int RoleId,
    string FullName,
    string PhoneNumber,
    string UserName,
    string PasswordHash);

public sealed class DuplicateEmployeeAccountException : Exception
{
    public DuplicateEmployeeAccountException() : base("The phone number or username is already in use.")
    {
    }
}
