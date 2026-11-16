// Models/DispatchSetupModels.cs
// Dispatch phase 6 - setup pages (port of YA Pages/Dispatch/IssueType, Approver, AssignGateMan, Steps, Status).
// Column names = YA tables DP_ISSUETYPE, DP_APPROVER, DP_ASSIGNGATEMAN, DP_STEPS, DP_STATUS.
namespace AlphaCare.Models
{
    // DP_ISSUETYPE row of the setup list (old IssueType.aspx)
    public class IssueTypeSetupModel
    {
        public int ID { get; set; }
        public string? ISSUETYPENAME { get; set; }
        public int RETURNABLE { get; set; }
        public int ISEXTERNAL { get; set; }
        public int ISPERSONAL { get; set; }
        public int ISACTIVE { get; set; }
    }

    // DP_APPROVER + approver / location / company / department (old dalApprover.GetAll / GetById)
    public class ApproverSetupModel
    {
        public int ID { get; set; }
        public string? APPROVERID { get; set; }
        public int? PRIORITY { get; set; }
        public int? BUSINESSCENTERID { get; set; }
        public int? COMPANYID { get; set; }
        public int? DEPARTMENTID { get; set; }
        public int? ISFINALAPPROVER { get; set; }
        public string? EMPLOYEENAME { get; set; }
        public string? OFFICEMOBILE { get; set; }
        public string? OFFICEMAIL { get; set; }
        public string? CENTERNAME { get; set; }
        public string? COMPANYNAME { get; set; }
        public string? DEPARTMENTCODE { get; set; }
        public string? DEPARTMENTNAME { get; set; }
        public string? DESIGNATIONNAME { get; set; }
    }

    // DP_ASSIGNGATEMAN + gate man (old dalGateman.GetAll / GetById)
    public class GateManSetupModel
    {
        public int ID { get; set; }
        public string? STAFFID { get; set; }
        public int? GATENO { get; set; }
        public int? BUSINESSCENTERID { get; set; }
        public string? EMPLOYEENAME { get; set; }
        public string? OFFICEMOBILE { get; set; }
        public string? OFFICEMAIL { get; set; }
        public string? CENTERNAME { get; set; }
        public string? DESIGNATIONNAME { get; set; }
    }

    // DP_STEPS (old Steps.aspx)
    public class StepSetupModel
    {
        public int ID { get; set; }
        public string? STEPNAME { get; set; }
        public int? PRIORITY { get; set; }
        public int? ISSUETYPEID { get; set; }
        public string? ISSUETYPENAME { get; set; }
        public int? ISFINALSTEP { get; set; }
    }

    // DP_STATUS (old Status.aspx)
    public class StatusSetupModel
    {
        public int ID { get; set; }
        public string? STATUSNAME { get; set; }
    }

    // BS_COMPANY drop down (old ddlCompany)
    public class DispatchCompanyModel
    {
        public int ID { get; set; }
        public string? COMPANYNAME { get; set; }
    }

    // approver page: list of one location (old ddlBusinessCenter -> rpt)
    public class ApproverSetupViewModel
    {
        public int CenterId { get; set; }
        public List<ApproverSetupModel> Rows { get; set; } = new();
        public List<BusinessCenterModel> BusinessCenters { get; set; } = new();
        public List<DepartmentModel> Departments { get; set; } = new();
        public List<DispatchCompanyModel> Companies { get; set; } = new();
    }

    // gate man page: list of one location
    public class GateManSetupViewModel
    {
        public int CenterId { get; set; }
        public List<GateManSetupModel> Rows { get; set; } = new();
        public List<BusinessCenterModel> BusinessCenters { get; set; } = new();
    }

    // steps page
    public class StepSetupViewModel
    {
        public List<StepSetupModel> Rows { get; set; } = new();
        public List<IssueTypeSetupModel> IssueTypes { get; set; } = new();
    }
}
