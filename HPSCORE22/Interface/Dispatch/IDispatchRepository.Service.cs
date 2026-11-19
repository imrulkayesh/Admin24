// Interface/Dispatch/IDispatchRepository.Service.cs
// Used by the Service module (old AssignService.aspx "Forward"): YA called dalDispatch.DeleteDispatchItems before it
// filled the user's open dispatch draft with the service items. New file only - no existing Dispatch file is changed.
namespace AlphaCare.Interface
{
    public partial interface IDispatchRepository
    {
        /// <summary>Old dalDispatch.DeleteDispatchItems(dispatchId): removes the items of the user's OWN OPEN draft only</summary>
        int ClearDraftItems(long dispatchId, string staffId);
    }
}
