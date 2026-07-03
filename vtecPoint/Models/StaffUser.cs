namespace vtecPoint.Models;

public class StaffUser
{
    public int StaffId { get; set; }
    public string StaffLogin { get; set; } = "";
    public string StaffName { get; set; } = "";
}

public class LoginRequest
{
    public string StaffLogin { get; set; } = "";
    public string StaffPassword { get; set; } = "";
}
