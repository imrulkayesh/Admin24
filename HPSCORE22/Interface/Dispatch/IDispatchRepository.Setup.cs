// Interface/Dispatch/IDispatchRepository.Setup.cs
// Dispatch phase 6 - setup pages (old IssueType.aspx, Approver.aspx, AssignGateMan.aspx, Steps.aspx, Status.aspx).
using AlphaCare.Models;

namespace AlphaCare.Interface
{
    public partial interface IDispatchRepository
    {
        // ---------- issue type ----------
        List<IssueTypeSetupModel> GetIssueTypesForSetup();
        bool IssueTypeExists(int id);
        bool IssueTypeNameExists(string name, int exceptId);
        int InsertIssueType(string name, bool returnable, bool external, bool personal, string staffId);
        int UpdateIssueType(int id, string name, bool returnable, bool external, bool personal, string staffId);

        // ---------- approver (DP_APPROVER) ----------
        List<DispatchCompanyModel> GetCompanies();
        /// <summary>Old dalApprover.GetAll(businessCenterId)</summary>
        List<ApproverSetupModel> GetApprovers(int businessCenterId);
        bool ApproverRowExists(int id);
        bool ApproverDuplicate(int exceptId, string approverId, int companyId, int centerId, int departmentId, int priority);
        int InsertApprover(int companyId, string approverId, int priority, int centerId, int departmentId, bool isFinal, string staffId);
        int UpdateApprover(int id, int companyId, string approverId, int priority, int centerId, int departmentId, bool isFinal, string userId);
        /// <summary>Old Common.Delete("DP_APPROVER", id) - removes the row</summary>
        int DeleteApprover(int id);

        // ---------- gate man (DP_ASSIGNGATEMAN) ----------
        /// <summary>Old dalGateman.GetAll(businessCenterId): active rows</summary>
        List<GateManSetupModel> GetGateMen(int businessCenterId);
        bool ActiveGateManExists(int id);
        bool GateManActiveElsewhere(string staffId, int exceptId);
        int InsertGateMan(string staffId, int gateNo, int centerId, string userId);
        int UpdateGateMan(int id, string staffId, int gateNo, int centerId, string userId);

        // ---------- steps (DP_STEPS) ----------
        List<StepSetupModel> GetSteps();
        bool StepExists(int id);
        bool StepNameExists(string name, int issueTypeId, int exceptId);
        int InsertStep(string name, int priority, int issueTypeId, bool isFinal, string staffId);
        int UpdateStep(int id, string name, int priority, int issueTypeId, bool isFinal, string staffId);

        // ---------- status (DP_STATUS) ----------
        List<StatusSetupModel> GetStatuses();
        bool StatusExists(int id);
        bool StatusNameExists(string name, int exceptId);
        int InsertStatus(string name);
        int UpdateStatus(int id, string name);
    }
}
