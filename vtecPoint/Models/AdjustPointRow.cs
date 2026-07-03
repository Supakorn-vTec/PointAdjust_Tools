namespace vtecPoint.Models;

public class AdjustPointRow
{
    public int RowNumber { get; set; }
    public string MemberCode { get; set; } = "";
    public decimal TranPoint { get; set; }
    public string Note { get; set; } = "";

    public int MemberId { get; set; }
    public string MemberName { get; set; } = "";
    public int CardId { get; set; }
    public string CardNo { get; set; } = "";
    public decimal PreviousPoint { get; set; }
    public decimal BalancePoint { get; set; }
    public bool HasSummaryPoint { get; set; }

    public bool IsValid { get; set; }
    public string VerifyRemark { get; set; } = "";
    public string HistoryUuid { get; set; } = "";
}

public class AdjustPointSettings
{
    public int StaffId { get; set; } = 1;
    public string StaffName { get; set; } = "Admin";
    public int PointType { get; set; } = 6;
    public int SyncStatus { get; set; } = 0;
    public int ShopId { get; set; } = 0;
    public DateTime SaleDate { get; set; } = DateTime.Today;
}

public class MemberPointInfo
{
    public int MemberId { get; set; }
    public string MemberCode { get; set; } = "";
    public string MemberName { get; set; } = "";
    public int Activated { get; set; }
    public int Deleted { get; set; }
    public int CardId { get; set; }
    public string CardNo { get; set; } = "";
    public decimal TotalPoint { get; set; }
    public bool HasSummaryPoint { get; set; }
}

public class PointTypeOption
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class AdjustPointResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string BatchUuid { get; set; } = "";
    public int HistoryRows { get; set; }
    public int SummaryRows { get; set; }
}
