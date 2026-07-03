using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using vtecPoint.Models;

namespace vtecPoint.Services;

public class StaffAuthService
{
    private readonly string _connectionString;

    public StaffAuthService(IConfiguration configuration)
    {
        _connectionString = configuration.GetSection("Database")["ConnectionString"]
            ?? throw new InvalidOperationException("Database:ConnectionString is not configured.");
    }

    public async Task<StaffUser?> ValidateLoginAsync(string login, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
            return null;

        const string sql = @"
SELECT StaffID, StaffLogin,
       CONCAT(StaffFirstName, ' ', StaffLastName) AS StaffName,
       StaffPassword
FROM staffs
WHERE (StaffLogin = @login OR StaffCode = @login)
  AND Activated = 1 AND Deleted = 0";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@login", login.Trim());

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        var storedPassword = reader.IsDBNull(3) ? "" : reader.GetValue(3)?.ToString()?.Trim() ?? "";
        if (!PasswordMatches(storedPassword, password))
            return null;

        return new StaffUser
        {
            StaffId = Convert.ToInt32(reader.GetValue(0)),
            StaffLogin = reader.GetString(1),
            StaffName = reader.IsDBNull(2) ? login.Trim() : reader.GetString(2).Trim()
        };
    }

    private static bool PasswordMatches(string stored, string plainPassword)
    {
        if (string.IsNullOrEmpty(stored))
            return false;

        if (string.Equals(stored, plainPassword, StringComparison.Ordinal))
            return true;

        var unicodeHash = HashPasswordUnicode(plainPassword);
        if (string.Equals(stored, unicodeHash, StringComparison.OrdinalIgnoreCase))
            return true;

        var utf8Hash = HashPasswordUtf8(plainPassword);
        return string.Equals(stored, utf8Hash, StringComparison.OrdinalIgnoreCase);
    }

    // VTEC backoffice Utilitys.HashPassword (FormsAuthentication SHA1, Unicode)
    public static string HashPasswordUnicode(string password)
    {
        var bytes = Encoding.Unicode.GetBytes(password);
        return Convert.ToHexString(SHA1.HashData(bytes));
    }

    // VTEC core VTECFunctions.Hash (SHA1, UTF8)
    public static string HashPasswordUtf8(string password)
    {
        var bytes = Encoding.UTF8.GetBytes(password);
        return Convert.ToHexString(SHA1.HashData(bytes));
    }
}
