using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace RestaurantManagement.DbTool;

/// <summary>
/// Sửa giá món qua màn hình Sửa món (/QuanLyMon/Sua/{id}) — nơi duy nhất còn sửa giá sau khi
/// màn hình "Sửa giá món" (/Management) được gộp vào Quản lý món. Giữ nguyên các thông tin khác của món.
/// </summary>
internal static class DishPriceEdit
{
    internal static async Task<HttpResponseMessage> Post(string connection, HttpClient client, int id, int price,
        bool withToken = true, params (string Name, string Value)[] extra)
    {
        string name, unit, description;
        int category, prep;
        await using (var cn = new SqlConnection(connection))
        {
            await cn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT Name,CategoryId,Unit,ISNULL(Description,N''),EstimatedPrepMinutes FROM dbo.MenuItems WHERE Id=@Id", cn);
            cmd.Parameters.AddWithValue("@Id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) throw new InvalidOperationException($"Menu item {id} not found.");
            (name, category, unit, description, prep) = (r.GetString(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetInt32(4));
        }
        var fields = new List<KeyValuePair<string, string>>
        {
            new("Mon.Id", id.ToString()), new("Mon.Ten", name), new("Mon.NhomMonId", category.ToString()),
            new("Mon.GiaBanVnd", price.ToString()), new("Mon.DonViTinh", unit),
            new("Mon.MoTaNgan", string.IsNullOrWhiteSpace(description) ? "Món demo" : description),
            new("Mon.ThoiGianCheBienPhut", prep.ToString()), new("Mon.TrangThai", "0")
        };
        if (withToken)
        {
            var edit = await client.GetStringAsync($"/QuanLyMon/Sua/{id}");
            fields.Add(new("__RequestVerificationToken",
                WebUtility.HtmlDecode(Regex.Match(edit, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value)));
        }
        fields.AddRange(extra.Select(e => new KeyValuePair<string, string>(e.Name, e.Value)));
        return await client.PostAsync($"/QuanLyMon/Sua/{id}", new FormUrlEncodedContent(fields));
    }
}
