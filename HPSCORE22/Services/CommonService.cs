using Microsoft.EntityFrameworkCore;
using Admin360.Repositories;
using QCMS.Repositories;

namespace Admin360.Services
{
    public class CommonService
    {
        private readonly CommonRepository _repo;

        public CommonService(CommonRepository repo)
        {
            _repo = repo;
        }

        public async Task<string> GenerateIdAsync(string tableName)
        {
            return await _repo.GenerateIdAsync(tableName);
        }
    }
}