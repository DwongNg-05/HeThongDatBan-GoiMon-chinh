using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.SqlClient;
using QRCoder;

namespace RestaurantManagement.Web.Services;

/// <summary>
/// Stores only a hash for QR lookup while keeping the public QR value available
/// for the manager to print again later.
/// </summary>
public sealed class TableQrService(IConfiguration configuration)
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Chưa cấu hình ConnectionStrings:DefaultConnection.");

    public async Task<TableQrToken?> GetActiveAsync(int tableId)
    {
        const string sql = """
            SELECT TOP (1) PublicToken, CreatedAt
            FROM dbo.TableQrCodes
            WHERE TableId = @TableId AND RevokedAt IS NULL AND PublicToken IS NOT NULL
            ORDER BY Id DESC;
            """;
        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@TableId", SqlDbType.Int).Value = tableId;
        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? new TableQrToken(reader.GetString(0), reader.GetDateTime(1))
            : null;
    }

    public async Task<TableQrToken> EnsureActiveAsync(int tableId, int actorUserId)
        => await GetActiveAsync(tableId) ?? await RotateAsync(tableId, actorUserId);

    public async Task<TableQrToken> RotateAsync(int tableId, int actorUserId)
    {
        // Token collisions are cryptographically negligible. Retrying keeps the
        // database unique constraints as the final source of truth.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var token = CreatePublicToken();
            try
            {
                await using var connection = new SqlConnection(ConnectionString);
                await using var command = new SqlCommand("dbo.usp_RotateTableQr", connection)
                    { CommandType = CommandType.StoredProcedure };
                command.Parameters.Add("@TableId", SqlDbType.Int).Value = tableId;
                command.Parameters.Add("@TokenHash", SqlDbType.Binary, 32).Value = Hash(token);
                command.Parameters.Add("@ActorUserId", SqlDbType.Int).Value = actorUserId;
                command.Parameters.Add("@PublicToken", SqlDbType.VarChar, 64).Value = token;
                await connection.OpenAsync();
                await command.ExecuteNonQueryAsync();
                return new TableQrToken(token, DateTime.UtcNow);
            }
            catch (SqlException ex) when ((ex.Number is 2601 or 2627) && attempt < 2)
            {
                // Generate another random public token and try again.
            }
        }

        throw new InvalidOperationException("Không thể sinh mã QR duy nhất cho bàn này.");
    }

    public async Task<TableQrDetails?> GetDetailsAsync(int tableId)
    {
        const string sql = """
            SELECT t.Id, t.Code, a.Name, t.MinCapacity, t.MaxCapacity, t.TableType, t.Status,
                   t.IsActive, qr.PublicToken, qr.CreatedAt
            FROM dbo.DiningTables t
            JOIN dbo.Areas a ON a.Id = t.AreaId
            OUTER APPLY
            (
                SELECT TOP (1) q.PublicToken, q.CreatedAt
                FROM dbo.TableQrCodes q
                WHERE q.TableId = t.Id AND q.RevokedAt IS NULL AND q.PublicToken IS NOT NULL
                ORDER BY q.Id DESC
            ) qr
            WHERE t.Id = @TableId;
            """;
        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@TableId", SqlDbType.Int).Value = tableId;
        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new TableQrDetails(
            reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
            reader.GetInt32(3), reader.GetInt32(4), reader.GetString(5), reader.GetString(6),
            reader.GetBoolean(7), reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetDateTime(9));
    }

    public async Task<TableQrLookup?> FindByPublicTokenAsync(string? token)
    {
        if (!IsValidPublicToken(token)) return null;

        const string sql = """
            SELECT t.Id, t.Code, a.Name, t.MinCapacity, t.MaxCapacity, t.TableType,
                   t.IsActive, q.RevokedAt
            FROM dbo.TableQrCodes q
            JOIN dbo.DiningTables t ON t.Id = q.TableId
            JOIN dbo.Areas a ON a.Id = t.AreaId
            WHERE q.TokenHash = @TokenHash;
            """;
        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@TokenHash", SqlDbType.Binary, 32).Value = Hash(token!);
        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new TableQrLookup(
            reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
            reader.GetInt32(3), reader.GetInt32(4), reader.GetString(5),
            reader.GetBoolean(6), !reader.IsDBNull(7));
    }

    public async Task<IReadOnlyList<TableQrPrintItem>> GetAreaPrintItemsAsync(int areaId, int actorUserId)
    {
        var items = await ReadAreaPrintItemsAsync(areaId);
        if (items.Count == 0) return items;

        // Old tables may predate the web QR feature. QR generation is limited to
        // the area the manager explicitly chose to export.
        foreach (var item in items.Where(item => item.PublicToken is null))
            await RotateAsync(item.TableId, actorUserId);

        return items.Any(item => item.PublicToken is null)
            ? await ReadAreaPrintItemsAsync(areaId)
            : items;
    }

    public static byte[] CreatePng(string content)
    {
        var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data);
        return png.GetGraphic(20);
    }

    public static string CreatePublicToken()
        => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(24));

    private async Task<List<TableQrPrintItem>> ReadAreaPrintItemsAsync(int areaId)
    {
        const string sql = """
            SELECT t.Id, t.Code, a.Name, qr.PublicToken
            FROM dbo.DiningTables t
            JOIN dbo.Areas a ON a.Id = t.AreaId
            OUTER APPLY
            (
                SELECT TOP (1) q.PublicToken
                FROM dbo.TableQrCodes q
                WHERE q.TableId = t.Id AND q.RevokedAt IS NULL AND q.PublicToken IS NOT NULL
                ORDER BY q.Id DESC
            ) qr
            WHERE t.AreaId = @AreaId AND t.IsActive = 1
            ORDER BY t.SortOrder, t.Code;
            """;
        var items = new List<TableQrPrintItem>();
        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@AreaId", SqlDbType.Int).Value = areaId;
        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            items.Add(new TableQrPrintItem(reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        return items;
    }

    /// <summary>SHA-256 của mã công khai; database chỉ tra cứu QR bằng giá trị này.</summary>
    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    public static bool IsValidPublicToken(string? token)
        => token is { Length: >= 16 and <= 64 }
           && token.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}

public sealed record TableQrToken(string PublicToken, DateTime CreatedAt);

public sealed record TableQrDetails(
    int TableId,
    string TableCode,
    string AreaName,
    int MinCapacity,
    int MaxCapacity,
    string TableType,
    string Status,
    bool IsActive,
    string? PublicToken,
    DateTime? CreatedAt);

public sealed record TableQrLookup(
    int TableId,
    string TableCode,
    string AreaName,
    int MinCapacity,
    int MaxCapacity,
    string TableType,
    bool IsActive,
    bool IsRevoked);

public sealed record TableQrPrintItem(int TableId, string TableCode, string AreaName, string? PublicToken);
