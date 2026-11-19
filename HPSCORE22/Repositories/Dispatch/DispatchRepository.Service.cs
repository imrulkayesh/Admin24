// Repositories/Dispatch/DispatchRepository.Service.cs
// Used by the Service module "Forward" (old dalDispatch.DeleteDispatchItems). New file only.
using Dapper;

namespace Admin360.Repositories
{
    public partial class DispatchRepository
    {
        // the old code deleted every item of the dispatch id it got; here only the items of the user's own open draft
        public int ClearDraftItems(long dispatchId, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                DELETE FROM DP_DISPATCHITEMS I
                 WHERE I.DISPATCHID = :Id
                   AND EXISTS (SELECT 1 FROM DP_DISPATCH D WHERE D.ID = I.DISPATCHID AND D.ISOPEN = 1 AND D.CREATEDBY = :StaffId)";
            return conn.Execute(sql, new { Id = dispatchId, StaffId = staffId });
        }
    }
}
