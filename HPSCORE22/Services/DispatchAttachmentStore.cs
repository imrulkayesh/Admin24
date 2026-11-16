// Services/DispatchAttachmentStore.cs
// Item attachments of the Dispatch module. The files live in the SAME folder YA uses (YA: ~/Content/Attachment/Dispatch/),
// so attachments made in YA open in Admin24 and the other way round. DP_DISPATCHITEMS.ATTACHMENT keeps only the file name.
//
// appsettings.json:  "Dispatch": { "AttachmentFolder": "\\\\server\\share\\YA\\Content\\Attachment\\Dispatch" }
// Empty / missing folder -> attachments are switched off (upload hidden, download says "not configured").
//
// Differences to YA (safety): a file never overwrites another file, script / program files are refused,
// files are only downloaded through a logged-in action (never as public static files).
using Microsoft.AspNetCore.StaticFiles;

namespace AlphaCare.Services
{
    public class DispatchAttachmentStore
    {
        public const long MaxBytes = 20L * 1024 * 1024;          // 20 MB per file
        private const int MaxNameLength = 100;                   // DP_DISPATCHITEMS.ATTACHMENT = NVARCHAR2(100)

        private static readonly HashSet<string> Blocked = new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".dll", ".bat", ".cmd", ".com", ".msi", ".ps1", ".vbs", ".js", ".jse", ".wsf", ".scr", ".jar",
            ".asp", ".aspx", ".ashx", ".asmx", ".cshtml", ".config", ".htm", ".html", ".svg", ".php", ".sh"
        };

        private readonly string? _folder;
        private readonly string? _gateEntryFolder;
        private readonly ILogger<DispatchAttachmentStore> _logger;
        private readonly FileExtensionContentTypeProvider _types = new();

        public DispatchAttachmentStore(IConfiguration config, ILogger<DispatchAttachmentStore> logger)
        {
            _logger = logger;
            _folder = FolderOf(config["Dispatch:AttachmentFolder"]);
            // visitor gate entry items: YA ~/Content/Attachment/GateEntry/  ("Dispatch": { "GateEntryAttachmentFolder": "..." })
            _gateEntryFolder = FolderOf(config["Dispatch:GateEntryAttachmentFolder"]);
        }

        private static string? FolderOf(string? folder) => string.IsNullOrWhiteSpace(folder) ? null : Path.GetFullPath(folder.Trim());

        public bool Enabled => _folder != null;
        public bool GateEntryEnabled => _gateEntryFolder != null;

        /// <summary>Saves the upload as "{dispatchId}_{file name}" (old YA name). Returns the stored file name, or an error text.</summary>
        public Task<(string? FileName, string? Error)> SaveAsync(IFormFile file, long dispatchId) => SaveToAsync(_folder, file, dispatchId);

        /// <summary>Gate entry item file "{gateEntryId}_{file name}" in the gate entry folder.</summary>
        public Task<(string? FileName, string? Error)> SaveGateEntryAsync(IFormFile file, long gateEntryId) => SaveToAsync(_gateEntryFolder, file, gateEntryId);

        public (string Path, string ContentType)? Find(string? storedName) => FindIn(_folder, storedName);
        public (string Path, string ContentType)? FindGateEntry(string? storedName) => FindIn(_gateEntryFolder, storedName);

        private async Task<(string? FileName, string? Error)> SaveToAsync(string? folder, IFormFile file, long dispatchId)
        {
            if (folder == null) return (null, "Attachments are not configured on this server.");
            if (file.Length <= 0) return (null, "The attached file is empty.");
            if (file.Length > MaxBytes) return (null, "The attached file is too big (maximum 20 MB).");

            string original = Path.GetFileName(file.FileName ?? "");
            foreach (char c in Path.GetInvalidFileNameChars()) original = original.Replace(c, '_');
            string ext = Path.GetExtension(original);
            if (string.IsNullOrWhiteSpace(original) || string.IsNullOrEmpty(ext)) return (null, "The attached file has no file type.");
            if (Blocked.Contains(ext)) return (null, $"Files of type {ext} are not allowed.");
            if (!Directory.Exists(folder)) return (null, "The attachment folder cannot be reached. Please contact Admin.");

            string baseName = Path.GetFileNameWithoutExtension(original);
            string prefix = dispatchId + "_";

            // never overwrite an existing file: "name.ext", "name(1).ext", ...
            for (int n = 0; n < 1000; n++)
            {
                string suffix = n == 0 ? "" : $"({n})";
                int room = MaxNameLength - prefix.Length - suffix.Length - ext.Length;
                if (room < 1) return (null, "The file name is too long.");
                string name = prefix + (baseName.Length > room ? baseName[..room] : baseName) + suffix + ext;
                string full = Path.Combine(folder, name);
                try
                {
                    await using var stream = new FileStream(full, FileMode.CreateNew, FileAccess.Write);   // CreateNew = fails if the file exists
                    await file.CopyToAsync(stream);
                    return (name, null);
                }
                catch (IOException) when (File.Exists(full))
                {
                    // name already used -> next number
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Dispatch attachment could not be saved. DispatchId={DispatchId}", dispatchId);
                    return (null, "The file could not be saved. Please try again.");
                }
            }
            return (null, "The file could not be saved (too many files with the same name).");
        }

        /// <summary>Full path + content type of a stored attachment, or null when it does not exist in the folder.</summary>
        private (string Path, string ContentType)? FindIn(string? folder, string? storedName)
        {
            if (folder == null || string.IsNullOrWhiteSpace(storedName)) return null;
            string name = Path.GetFileName(storedName.Trim());                      // no folder parts ("..\" etc.)
            string full = Path.GetFullPath(Path.Combine(folder, name));
            if (!full.StartsWith(folder, StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) return null;
            return (full, _types.TryGetContentType(name, out var t) ? t : "application/octet-stream");
        }
    }
}
