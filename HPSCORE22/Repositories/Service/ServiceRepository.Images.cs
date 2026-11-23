// Repositories/Service/ServiceRepository.Images.cs
// Images of a service request (SC_SERVICEIMAGE + SQ_SC_SERVICEIMAGE, App_Data/Sql/Service_Images.sql).
using AlphaCare.Models;
using Dapper;

namespace Admin360.Repositories
{
    public partial class ServiceRepository
    {
        private const string ImageSelect = @"
            SELECT I.ID, I.REQUESTID, I.FILENAME, I.ORIGINALNAME, I.CONTENTTYPE, I.FILESIZE, I.CREATEDBY, I.CREATEDATE, E.EMPLOYEENAME
              FROM SC_SERVICEIMAGE I
              LEFT JOIN EM_EMPLOYEE E ON E.STAFFID = I.CREATEDBY";

        public List<ServiceImageModel> GetImages(long requestId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Query<ServiceImageModel>(ImageSelect + " WHERE I.REQUESTID = :Rid AND I.ISACTIVE = 1 ORDER BY I.ID",
                                                 new { Rid = requestId }).ToList();
        }

        public ServiceImageModel? GetImage(long id)
        {
            using var conn = _databaseService.GetConnection();
            return conn.QueryFirstOrDefault<ServiceImageModel>(ImageSelect + " WHERE I.ID = :Id AND I.ISACTIVE = 1", new { Id = id });
        }

        public int CountImages(long requestId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.ExecuteScalar<int>("SELECT COUNT(1) FROM SC_SERVICEIMAGE WHERE REQUESTID = :Rid AND ISACTIVE = 1", new { Rid = requestId });
        }

        public long InsertImage(long requestId, string fileName, string originalName, string contentType, long fileSize, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();
            long id = conn.ExecuteScalar<long>("SELECT SQ_SC_SERVICEIMAGE.NEXTVAL FROM DUAL", transaction: tx);
            conn.Execute(@"
                INSERT INTO SC_SERVICEIMAGE (ID, REQUESTID, FILENAME, ORIGINALNAME, CONTENTTYPE, FILESIZE, ISACTIVE, CREATEDBY, CREATEDATE)
                VALUES (:Id, :Rid, :Fn, :Orig, :Ct, :Sz, 1, :StaffId, SYSDATE)",
                new { Id = id, Rid = requestId, Fn = fileName, Orig = originalName, Ct = contentType, Sz = fileSize, StaffId = staffId }, tx);
            tx.Commit();
            return id;
        }

        public int RemoveImage(long id, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute("UPDATE SC_SERVICEIMAGE SET ISACTIVE = 0, UPDATEDBY = :StaffId, UPDATEDATE = SYSDATE WHERE ID = :Id AND ISACTIVE = 1",
                                new { StaffId = staffId, Id = id });
        }
    }
}
