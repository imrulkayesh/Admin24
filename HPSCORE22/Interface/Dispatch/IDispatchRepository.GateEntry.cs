// Interface/Dispatch/IDispatchRepository.GateEntry.cs
// Dispatch phase 8 - visitor gate entry + notice board (port of YA dalGateEntry, dalNoticeBorad and the two gate entry reports).
using AlphaCare.Models;

namespace AlphaCare.Interface
{
    public partial interface IDispatchRepository
    {
        // ---------------- visitor gate entry ----------------
        /// <summary>Old CheckGateEntry: the user's open gate entry (ISOPEN = 1), created when there is none.</summary>
        long GetOrCreateGateEntryDraft(string staffId);
        bool IsOwnGateEntryDraft(long entryId, string staffId);
        GateEntryItemModel? GetGateEntryItem(long itemId);
        /// <summary>Gate entry id (DP_GATEENTRY.ID) of an item.</summary>
        long? GetGateEntryIdOfItem(long itemId);
        int AddGateEntryItem(long entryId, string staffId, string itemName, decimal quantity, string unit, string remarks, bool sample, bool returnable, string attachment);
        int DeleteGateEntryItem(long itemId, string staffId);
        List<string> SearchGateEntryItems(string term);
        /// <summary>Old dalGateEntry.Update: the draft becomes an entry "R" + location + yyMMdd + 6 digits. Returns the GATEENTRYID or null.</summary>
        string? SubmitGateEntry(SaveGateEntryRequest r, string staffId, string userId, int userBusinessCenterId);
        GateEntryDetailModel? GetGateEntry(long entryId);
        /// <summary>The user made the entry, or is its host.</summary>
        bool IsGateEntryPerson(long entryId, string staffId);
        /// <summary>Old GetSampleReturnable: my entries with a returnable sample that is not returned yet.</summary>
        List<GateEntryListItemModel> GetMyReturnableSamples(string staffId);
        /// <summary>Old SampleReturn (only the host, only while not returned).</summary>
        bool ReturnSample(long entryId, string staffId);

        // ---------------- notice board ----------------
        List<DispatchTypeModel> GetDispatchTypes();
        long GetOrCreateNoticeDraft(string staffId);
        bool IsOwnNoticeDraft(long noticeId, string staffId);
        NoticeItemModel? GetNoticeItem(long itemId);
        List<NoticeItemModel> GetNoticeItems(long noticeId);
        bool NoticeDraftHasItem(long noticeId, string itemName);
        int AddNoticeItem(long noticeId, string staffId, string itemName, decimal quantity, string unit, string notes, string attachment);
        int UpdateNoticeItem(long itemId, string staffId, decimal quantity, string unit, string notes);
        int DeleteNoticeItem(long itemId, string staffId);
        List<string> SearchNoticeItems(string term);
        bool TrackingNoUsed(string trackingNo, long exceptId);
        /// <summary>Old dalNoticeBorad.Update. False when the draft is not the user's open draft or has no item.</summary>
        bool SubmitNotice(SaveNoticeRequest r, decimal lcAmount, string staffId, string userId);
        NoticeDetailModel? GetNotice(long noticeId);
        NoticeEmployeeModel? GetNoticeEmployee(string staffId);
        /// <summary>Old UpdateReceiver: only while ACTIVE = 1 (not handed over yet).</summary>
        bool ReceiveNotice(NoticeReceiveRequest r, string userId);

        // ---------------- reports (at most DispatchRepository.ReportMaxRows rows) ----------------
        /// <summary>Old Pages/DispatchReport/GateEntryReport.aspx</summary>
        List<IDictionary<string, object?>> ReportGateEntry(DispatchReportFilter f);
        /// <summary>Old Pages/DispatchReport/GateEntryReturnItem.aspx</summary>
        List<IDictionary<string, object?>> ReportGateEntryReturned(DispatchReportFilter f);
        /// <summary>Old DNoticeBoardView.aspx / RNItems.aspx search</summary>
        List<IDictionary<string, object?>> ReportNoticeBoard(DispatchReportFilter f);
    }
}
