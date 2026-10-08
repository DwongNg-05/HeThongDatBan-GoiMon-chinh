using Microsoft.Data.SqlClient;
namespace RestaurantManagement.DbTool;
internal static partial class DatabaseTool
{
    internal static async Task SeedConfirmationDemo(string connection)
    {
        await using var cn = new SqlConnection(connection);
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
        try
        {
            await Batches(cn, tx, await File.ReadAllTextAsync(Path.Combine(Root,"database","seeds","ReservationConfirmationDemo.sql")));
            await tx.CommitAsync();
            Console.WriteLine("Confirmation demo ready: CF0001, DEMO-5 (4 seats), DEMO-2 (2 seats). Existing rows preserved.");
        }
        catch { await tx.RollbackAsync(); throw; }
    }
}
