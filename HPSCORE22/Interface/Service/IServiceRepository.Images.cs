// Interface/Service/IServiceRepository.Images.cs
// Images of a service request (SC_SERVICEIMAGE - new table, App_Data/Sql/Service_Images.sql).
using AlphaCare.Models;

namespace AlphaCare.Interface
{
    public partial interface IServiceRepository
    {
        /// <summary>Active images of a request, oldest first</summary>
        List<ServiceImageModel> GetImages(long requestId);
        ServiceImageModel? GetImage(long id);
        int CountImages(long requestId);
        long InsertImage(long requestId, string fileName, string originalName, string contentType, long fileSize, string staffId);
        /// <summary>Soft delete (ISACTIVE = 0): the row and the file are kept</summary>
        int RemoveImage(long id, string staffId);
    }
}
