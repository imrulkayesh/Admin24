// Controllers/Service/ServiceController.Images.cs
// Images of a service request (SC_SERVICEIMAGE). Files in the same folder as the item attachments (Service:AttachmentFolder).
//   - New Service: the requester adds images to his open request (draft) before Save
//   - View Service: people on the request add more images until it is completed; everybody who may open the request sees them
//   - an image is removed by the person who added it (draft: the owner of the draft); removal is a soft delete
using AlphaCare.Models;
using AlphaCare.Services;
using Microsoft.AspNetCore.Mvc;

namespace QCMS.Controllers
{
    public partial class ServiceController
    {
        private const int MaxImagesPerRequest = 20;
        private const int MaxImagesPerUpload = 10;

        // may see the images: owner of the draft, people on the request, Service Request Report users (submitted requests)
        private bool CanSeeImages(long requestId) =>
            _repo.IsOwnDraft(requestId, StaffId) || _repo.CanView(requestId, StaffId)
            || (Allowed(ReportRequestUrl) && _repo.GetRequestDetails(requestId) != null);

        // may add images: owner of the draft, or people on a submitted request that is not completed
        private bool CanChangeImages(long requestId)
        {
            if (_repo.IsOwnDraft(requestId, StaffId)) return true;
            if (!_repo.CanView(requestId, StaffId)) return false;
            var d = _repo.GetRequestDetails(requestId);
            return d != null && d.STATUS != ServiceStatus.Completed;
        }

        private object ImageRows(long requestId) =>
            _repo.GetImages(requestId).Select(i => new
            {
                id = i.ID,
                url = Url.Action(nameof(ServiceImage), new { id = i.ID }),
                name = string.IsNullOrWhiteSpace(i.ORIGINALNAME) ? i.FILENAME : i.ORIGINALNAME,
                by = i.EMPLOYEENAME ?? i.CREATEDBY,
                date = i.CREATEDATE?.ToString("dd/MM/yyyy hh:mm tt"),
                mine = i.CREATEDBY == StaffId
            }).ToList();

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(MaxImagesPerUpload * ServiceAttachmentStore.MaxImageBytes + 1024 * 1024)]
        public async Task<IActionResult> UploadImages(long requestId, List<IFormFile>? files)
        {
            files = (files ?? new List<IFormFile>()).Where(f => f != null && f.Length > 0).ToList();
            if (files.Count == 0) return Fail("Choose at least one image.");
            if (files.Count > MaxImagesPerUpload) return Fail($"Add at most {MaxImagesPerUpload} images at a time.");
            if (!_files.Enabled) return Fail("Attachments are not configured on this server.");
            if (!CanChangeImages(requestId)) return Fail("You cannot add images to this request.");

            int have = _repo.CountImages(requestId);
            if (have + files.Count > MaxImagesPerRequest)
                return Fail($"A request can have at most {MaxImagesPerRequest} images ({have} already added).");

            var errors = new List<string>();
            int added = 0;
            foreach (var f in files)
            {
                try
                {
                    var saved = await _files.SaveImageAsync(f, requestId);
                    if (saved.Error != null) { errors.Add(saved.Error); continue; }
                    string original = Path.GetFileName(f.FileName ?? "");
                    if (original.Length > 200) original = original[^200..];
                    _repo.InsertImage(requestId, saved.FileName!, original, saved.ContentType!, f.Length, StaffId);
                    added++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Service image upload failed. RequestId={RequestId}", requestId);
                    errors.Add($"{f.FileName}: could not be saved.");
                }
            }

            string msg = added > 0 ? $"{added} image(s) added." : "No image was added.";
            if (errors.Count > 0) msg += " " + string.Join(" ", errors);
            return Json(new { success = added > 0, message = msg, images = ImageRows(requestId) });
        }

        // the image itself (shown in the page, opened in a new tab / the preview)
        [HttpGet]
        public IActionResult ServiceImage(long id)
        {
            var img = _repo.GetImage(id);
            if (img == null) return NotFound();
            if (!CanSeeImages(img.REQUESTID)) return RedirectToAction("Denied", "Accounts");
            var f = _files.Find(img.FILENAME);
            if (f == null) return NotFound(_files.Enabled ? "The image file was not found." : "Attachments are not configured on this server.");
            Response.Headers["Cache-Control"] = "private, max-age=3600";
            return PhysicalFile(f.Value.Path, string.IsNullOrWhiteSpace(img.CONTENTTYPE) ? f.Value.ContentType : img.CONTENTTYPE);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteImage(long id)
        {
            var img = _repo.GetImage(id);
            if (img == null) return Fail("Image not found. Please reload the page.");
            bool draft = _repo.IsOwnDraft(img.REQUESTID, StaffId);
            if (!draft && (img.CREATEDBY != StaffId || !CanChangeImages(img.REQUESTID)))
                return Fail("Only the person who added the image can remove it (until the request is completed).");
            try
            {
                _repo.RemoveImage(id, StaffId);
                return Json(new { success = true, message = "Image removed.", images = ImageRows(img.REQUESTID) });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Service image delete failed. Id={Id}", id);
                return Fail("The image could not be removed. Please try again.");
            }
        }
    }
}
