using RetailCare.Models;

namespace AlphaCare.Models.Dashboard_Model
{
    public class UserDashboardViewModel
    {
        public UserInfoModel Userinformation { get; set; }=new UserInfoModel();
        public List<ModuleModel> Modules { get; set; } = new();
        public int? SelectedModuleId { get; set; }
    }
   
}
