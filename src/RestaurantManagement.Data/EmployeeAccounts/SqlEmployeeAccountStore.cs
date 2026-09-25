using Microsoft.Data.SqlClient;

namespace RestaurantManagement.Data;

public sealed class SqlEmployeeAccountStore(string? connectionString) : IEmployeeAccountStore
{
    private const string InternalRolesFilter = "('Manager', 'Waiter', 'Kitchen', 'Cashier')";
    private readonly string? _connectionString = connectionString;

    public async Task<IReadOnlyList<EmployeeRole>> GetInternalRolesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = $"SELECT Id, Name FROM dbo.Roles WHERE Code IN {InternalRolesFilter} ORDER BY Id;";
        var roles = new List<EmployeeRole>();

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            roles.Add(new EmployeeRole(reader.GetInt32(0), reader.GetString(1)));
        }

        return roles;
    }

    public Task<bool> IsInternalRoleAsync(int roleId, CancellationToken cancellationToken = default) =>
        ExistsAsync($"SELECT 1 FROM dbo.Roles WHERE Id = @value AND Code IN {InternalRolesFilter};", roleId, cancellationToken);

    public Task<bool> PhoneNumberExistsAsync(string phoneNumber, CancellationToken cancellationToken = default) =>
        ExistsAsync("SELECT 1 FROM dbo.Users WHERE Phone = @value;", phoneNumber, cancellationToken);

    public Task<bool> UserNameExistsAsync(string userName, CancellationToken cancellationToken = default) =>
        ExistsAsync("SELECT 1 FROM dbo.Users WHERE NormalizedUserName = @value;", userName.Trim().ToUpperInvariant(), cancellationToken);

    public async Task CreateAsync(NewEmployeeAccount account, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT dbo.Users (RoleId, FullName, UserName, Phone, PasswordHash, MustChangePassword, IsActive)
            VALUES (@roleId, @fullName, @userName, @phoneNumber, @passwordHash, 1, 1);
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@roleId", account.RoleId);
        command.Parameters.AddWithValue("@fullName", account.FullName);
        command.Parameters.AddWithValue("@userName", account.UserName);
        command.Parameters.AddWithValue("@phoneNumber", account.PhoneNumber);
        command.Parameters.AddWithValue("@passwordHash", account.PasswordHash);

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            throw new DuplicateEmployeeAccountException();
        }
    }

    private async Task<bool> ExistsAsync(string sql, object value, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@value", value);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            throw new InvalidOperationException("Thiếu chuỗi kết nối. Hãy cấu hình RM_CONNECTION_STRING hoặc ConnectionStrings:RestaurantManagement.");
        }

        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
