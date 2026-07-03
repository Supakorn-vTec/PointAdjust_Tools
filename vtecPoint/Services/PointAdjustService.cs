using System.Data;
using System.Text;
using ExcelDataReader;
using Microsoft.Data.SqlClient;
using vtecPoint.Models;

namespace vtecPoint.Services;

public class PointAdjustService
{
    private readonly string _connectionString;

    public PointAdjustService(IConfiguration configuration)
    {
        _connectionString = configuration.GetSection("Database")["ConnectionString"]
            ?? throw new InvalidOperationException("Database:ConnectionString is not configured.");
    }

    public async Task<List<PointTypeOption>> GetPointTypesAsync(CancellationToken ct = default)
    {
        var types = new List<PointTypeOption>();
        try
        {
            const string sql = "SELECT PointTypeID, PointTypeName FROM RewardPointType ORDER BY PointTypeID";
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                types.Add(new PointTypeOption
                {
                    Id = ReadInt(reader, 0),
                    Name = reader.IsDBNull(1) ? $"Type {reader.GetValue(0)}" : reader.GetString(1)
                });
            }
        }
        catch
        {
            // ponytail: fallback if table/columns differ
        }

        if (types.Count == 0)
        {
            types.AddRange(new[]
            {
                new PointTypeOption { Id = 1, Name = "Earn" },
                new PointTypeOption { Id = 2, Name = "Redeem" },
                new PointTypeOption { Id = 3, Name = "Void Earn" },
                new PointTypeOption { Id = 4, Name = "Void Redeem" },
                new PointTypeOption { Id = 6, Name = "Adjust Point" },
            });
        }

        return types;
    }

    public List<AdjustPointRow> ParseExcel(Stream stream)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        using var reader = ExcelReaderFactory.CreateReader(stream);
        var table = reader.AsDataSet(new ExcelDataSetConfiguration
        {
            ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = true }
        }).Tables[0];

        var memberCol = FindColumn(table, "MemberCode");
        var pointCol = FindColumn(table, "Point");
        var noteCol = FindColumn(table, "Note");

        if (memberCol < 0 || pointCol < 0)
            throw new InvalidOperationException("Excel must have columns: MemberCode, Point (Note is optional).");

        var rows = new List<AdjustPointRow>();
        for (var i = 0; i < table.Rows.Count; i++)
        {
            var dr = table.Rows[i];
            var memberCode = FormatMemberCode(dr[memberCol]);
            if (string.IsNullOrWhiteSpace(memberCode))
                continue;

            if (!decimal.TryParse(dr[pointCol]?.ToString(), out var tranPoint))
                tranPoint = 0;

            rows.Add(new AdjustPointRow
            {
                RowNumber = i + 2,
                MemberCode = memberCode,
                TranPoint = tranPoint,
                Note = noteCol >= 0 ? dr[noteCol]?.ToString()?.Trim() ?? "" : "",
                HistoryUuid = Guid.NewGuid().ToString().ToUpper()
            });
        }

        return rows;
    }

    public async Task<List<AdjustPointRow>> VerifyAsync(List<AdjustPointRow> rows, CancellationToken ct = default)
    {
        var members = await LoadMembersAsync(rows.Select(r => r.MemberCode).Distinct(), ct);
        var memberLookup = members
            .GroupBy(m => m.MemberCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var runningBalance = members.ToDictionary(
            m => m.MemberId,
            m => m.TotalPoint);

        foreach (var row in rows)
        {
            row.IsValid = true;
            row.VerifyRemark = "";

            if (row.TranPoint == 0)
            {
                MarkInvalid(row, "Point is zero.");
                continue;
            }

            if (!memberLookup.TryGetValue(row.MemberCode, out var matches))
            {
                MarkInvalid(row, "Member not found.");
                continue;
            }

            if (matches.Count > 1)
            {
                MarkInvalid(row, "Duplicate member code in database.");
                continue;
            }

            var member = matches[0];
            if (member.Deleted != 0)
            {
                MarkInvalid(row, "Member is deleted.");
                continue;
            }

            if (member.Activated == 0)
            {
                MarkInvalid(row, "Member is not activated.");
                continue;
            }

            if (member.CardId == 0)
            {
                MarkInvalid(row, "Member card not found.");
                continue;
            }

            row.MemberId = member.MemberId;
            row.MemberName = member.MemberName;
            row.CardId = member.CardId;
            row.CardNo = member.CardNo;
            row.HasSummaryPoint = member.HasSummaryPoint;

            var previous = runningBalance.GetValueOrDefault(member.MemberId, member.TotalPoint);
            row.PreviousPoint = previous;
            row.BalancePoint = Math.Max(0, previous + row.TranPoint);
            runningBalance[member.MemberId] = row.BalancePoint;
        }

        return rows;
    }

    public async Task<AdjustPointResult> ConfirmAsync(
        List<AdjustPointRow> rows,
        AdjustPointSettings settings,
        CancellationToken ct = default)
    {
        var validRows = rows.Where(r => r.IsValid).ToList();
        if (validRows.Count == 0)
            return new AdjustPointResult { Success = false, Message = "No valid rows to import." };

        if (validRows.Count != rows.Count)
            return new AdjustPointResult { Success = false, Message = "Some rows are invalid. Fix or remove them before confirming." };

        var batchUuid = Guid.NewGuid().ToString().ToUpper();
        var shopInfo = await LoadShopInfoAsync(settings.ShopId, ct);
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var saleDate = settings.SaleDate.ToString("yyyy-MM-dd");

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);

        try
        {
            var historyRows = 0;
            const int batchSize = 500;

            for (var offset = 0; offset < validRows.Count; offset += batchSize)
            {
                var chunk = validRows.Skip(offset).Take(batchSize).ToList();
                historyRows += await InsertHistoryBatchAsync(conn, tx, chunk, settings, shopInfo, batchUuid, saleDate, now, ct);
            }

            var summaryRows = await UpsertSummaryAsync(conn, tx, validRows, now, ct);

            await tx.CommitAsync(ct);

            return new AdjustPointResult
            {
                Success = true,
                Message = $"Imported {historyRows} history rows, updated {summaryRows} summary rows.",
                BatchUuid = batchUuid,
                HistoryRows = historyRows,
                SummaryRows = summaryRows
            };
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            return new AdjustPointResult { Success = false, Message = ex.Message };
        }
    }

    private static string FormatMemberCode(object? value)
    {
        if (value == null || value == DBNull.Value) return "";
        return value switch
        {
            double d => ((long)d).ToString(),
            float f => ((long)f).ToString(),
            decimal m => ((long)m).ToString(),
            _ => value.ToString()?.Trim() ?? ""
        };
    }

    private static int FindColumn(DataTable table, string name)
    {
        for (var i = 0; i < table.Columns.Count; i++)
        {
            if (string.Equals(table.Columns[i].ColumnName?.Trim(), name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static void MarkInvalid(AdjustPointRow row, string remark)
    {
        row.IsValid = false;
        row.VerifyRemark = remark;
    }

    private async Task<List<MemberPointInfo>> LoadMembersAsync(IEnumerable<string> memberCodes, CancellationToken ct)
    {
        var codes = memberCodes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var result = new List<MemberPointInfo>();

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        const int chunkSize = 800;
        for (var i = 0; i < codes.Count; i += chunkSize)
        {
            var chunk = codes.Skip(i).Take(chunkSize).ToList();
            var paramNames = chunk.Select((_, idx) => $"@p{idx}").ToArray();
            var sql = $@"
SELECT a.MemberID, a.MemberCode,
       CONCAT(a.MemberFirstName, ' ', a.MemberLastName) AS MemberFullName,
       a.Activated, a.Deleted,
       CASE WHEN b.CardID IS NULL THEN 0 ELSE b.CardID END AS CardID,
       CASE WHEN b.CardNumber IS NULL THEN '' ELSE b.CardNumber END AS CardNumber,
       CASE WHEN c.TotalPoint IS NULL THEN 0 ELSE c.TotalPoint END AS TotalPoint,
       CASE WHEN c.TotalPoint IS NULL THEN 0 ELSE 1 END AS HasSummaryPoint
FROM members a
LEFT JOIN member_card b ON a.MemberID = b.MemberID
LEFT JOIN RewardPointSummary c ON a.MemberID = c.MemberID AND b.CardID = c.CardID
WHERE a.MemberCode IN ({string.Join(", ", paramNames)})";

            await using var cmd = new SqlCommand(sql, conn);
            for (var j = 0; j < chunk.Count; j++)
                cmd.Parameters.AddWithValue(paramNames[j], chunk[j]);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                result.Add(new MemberPointInfo
                {
                    MemberId = ReadInt(reader, 0),
                    MemberCode = reader.GetString(1),
                    MemberName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Activated = ReadInt(reader, 3),
                    Deleted = ReadInt(reader, 4),
                    CardId = ReadInt(reader, 5),
                    CardNo = reader.IsDBNull(6) ? "" : reader.GetString(6),
                    TotalPoint = ReadDecimal(reader, 7),
                    HasSummaryPoint = ReadInt(reader, 8) == 1
                });
            }
        }

        return result;
    }

    private async Task<(int MerchantId, int BrandId, int ShopId, string ShopCode, string ShopName)> LoadShopInfoAsync(
        int shopId,
        CancellationToken ct)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = shopId > 0
            ? "SELECT TOP 1 MerchantID, BrandID, ShopID, ShopCode, ShopName FROM shop_data WHERE ShopID = @shopId"
            : "SELECT TOP 1 MerchantID, BrandID, ShopID, ShopCode, ShopName FROM shop_data WHERE Deleted = 0 ORDER BY ShopID";

        await using var cmd = new SqlCommand(sql, conn);
        if (shopId > 0)
            cmd.Parameters.AddWithValue("@shopId", shopId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return (0, 0, 0, "", "");

        return (
            ReadInt(reader, 0),
            ReadInt(reader, 1),
            ReadInt(reader, 2),
            reader.IsDBNull(3) ? "" : reader.GetString(3),
            reader.IsDBNull(4) ? "" : reader.GetString(4));
    }

    // ponytail: SQL tinyint/smallint → Convert instead of GetInt32
    private static int ReadInt(SqlDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));

    private static decimal ReadDecimal(SqlDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? 0 : Convert.ToDecimal(reader.GetValue(ordinal));

    private static async Task<int> InsertHistoryBatchAsync(
        SqlConnection conn,
        SqlTransaction tx,
        List<AdjustPointRow> rows,
        AdjustPointSettings settings,
        (int MerchantId, int BrandId, int ShopId, string ShopCode, string ShopName) shop,
        string batchUuid,
        string saleDate,
        string now,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.Append(@"INSERT INTO RewardPointHistory (
HistoryUUID, TransactionID, ComputerID, PayDetailID, TranKey, PointType,
CardID, CardNo, ShopID, ShopCode, ShopName, BrandID, MerchantID, SaleDate,
ReceiptNumber, ReceiptRetailPrice, ReceiptDiscount, ReceiptNetSale, ReceiptPayPrice, TotalPointPrice,
PreviousPoint, TranPoint, BalancePoint, MemberID, MemberName, StaffID, StaffName,
HistoryDateTime, InsertAtHQDateTime, SyncResponseDateTime, SyncInfo, SyncStatus,
SubSystemReqID, SubSystemResponse, SubSystemResponseDateTime) VALUES ");

        await using var cmd = new SqlCommand { Connection = conn, Transaction = tx };

        for (var i = 0; i < rows.Count; i++)
        {
            if (i > 0) sb.Append(", ");

            var row = rows[i];
            var prefix = $"r{i}";
            sb.Append($"(@{prefix}HistoryUuid, 0, 0, 0, '', @pointType, @{prefix}CardId, @{prefix}CardNo, @shopId, @shopCode, @shopName, @brandId, @merchantId, @saleDate, ");
            sb.Append($"NULL, 0, 0, 0, 0, 0, @{prefix}Previous, @{prefix}Tran, @{prefix}Balance, @{prefix}MemberId, @{prefix}MemberName, @staffId, @staffName, ");
            sb.Append($"@now, @now, @now, @{prefix}Note, @syncStatus, @batchUuid, NULL, @now)");

            cmd.Parameters.AddWithValue($"@{prefix}HistoryUuid", row.HistoryUuid);
            cmd.Parameters.AddWithValue($"@{prefix}CardId", row.CardId);
            cmd.Parameters.AddWithValue($"@{prefix}CardNo", row.CardNo);
            cmd.Parameters.AddWithValue($"@{prefix}Previous", row.PreviousPoint);
            cmd.Parameters.AddWithValue($"@{prefix}Tran", row.TranPoint);
            cmd.Parameters.AddWithValue($"@{prefix}Balance", row.BalancePoint);
            cmd.Parameters.AddWithValue($"@{prefix}MemberId", row.MemberId);
            cmd.Parameters.AddWithValue($"@{prefix}MemberName", row.MemberName);
            cmd.Parameters.AddWithValue($"@{prefix}Note", row.Note);
        }

        cmd.Parameters.AddWithValue("@pointType", settings.PointType);
        cmd.Parameters.AddWithValue("@shopId", shop.ShopId);
        cmd.Parameters.AddWithValue("@shopCode", shop.ShopCode);
        cmd.Parameters.AddWithValue("@shopName", shop.ShopName);
        cmd.Parameters.AddWithValue("@brandId", shop.BrandId);
        cmd.Parameters.AddWithValue("@merchantId", shop.MerchantId);
        cmd.Parameters.AddWithValue("@saleDate", saleDate);
        cmd.Parameters.AddWithValue("@staffId", settings.StaffId);
        cmd.Parameters.AddWithValue("@staffName", settings.StaffName);
        cmd.Parameters.AddWithValue("@now", now);
        cmd.Parameters.AddWithValue("@syncStatus", settings.SyncStatus);
        cmd.Parameters.AddWithValue("@batchUuid", batchUuid);

        cmd.CommandText = sb.ToString();
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<int> UpsertSummaryAsync(
        SqlConnection conn,
        SqlTransaction tx,
        List<AdjustPointRow> rows,
        string now,
        CancellationToken ct)
    {
        var latestByMember = rows
            .GroupBy(r => r.MemberId)
            .Select(g => g.Last())
            .ToList();

        var affected = 0;

        foreach (var row in latestByMember.Where(r => !r.HasSummaryPoint))
        {
            await using var insertCmd = new SqlCommand(
                "INSERT INTO RewardPointSummary (MemberID, CardID, TotalPoint, UpdateDate, OnlineStatus) VALUES (@memberId, @cardId, @total, @now, 1)",
                conn, tx);
            insertCmd.Parameters.AddWithValue("@memberId", row.MemberId);
            insertCmd.Parameters.AddWithValue("@cardId", row.CardId);
            insertCmd.Parameters.AddWithValue("@total", row.BalancePoint);
            insertCmd.Parameters.AddWithValue("@now", now);
            affected += await insertCmd.ExecuteNonQueryAsync(ct);
        }

        foreach (var row in latestByMember.Where(r => r.HasSummaryPoint))
        {
            await using var updateCmd = new SqlCommand(
                "UPDATE RewardPointSummary SET TotalPoint = @total, UpdateDate = @now WHERE MemberID = @memberId AND CardID = @cardId",
                conn, tx);
            updateCmd.Parameters.AddWithValue("@total", row.BalancePoint);
            updateCmd.Parameters.AddWithValue("@now", now);
            updateCmd.Parameters.AddWithValue("@memberId", row.MemberId);
            updateCmd.Parameters.AddWithValue("@cardId", row.CardId);
            affected += await updateCmd.ExecuteNonQueryAsync(ct);
        }

        return affected;
    }
}
