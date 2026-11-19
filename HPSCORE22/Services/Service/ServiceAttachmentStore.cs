// Services/ServiceAttachmentStore.cs
// Item attachments of the Service module. The files live in the SAME folder YA uses (YA: ~/Content/Attachment/Service/),
// so attachments made in YA open in Admin24 and the other way round. SC_SERVICEDETAILS.ATTACHMENT keeps only the file name.
//
// appsettings.json:  "Service": { "AttachmentFolder": "\\\\server\\share\\YA\\Content\\Attachment\\Service" }
// Empty / missing folder -> attachments are switched off (upload hidden, download says "not configured").
//
// Same safety rules as DispatchAttachmentStore: a file never overwrites another file, script / program files are refused,
// files are only downloaded through a logged-in action (never as public static files).
using Microsoft.AspNetCore.StaticFiles;
using System.Text;

namespace AlphaCare.Services
{
    public class ServiceAttachmentStore
    {
        public const long MaxBytes = 20L * 1024 * 1024;          // 20 MB per file
        private const int MaxNameBytes = 100;                    // SC_SERVICEDETAILS.ATTACHMENT = VARCHAR2(100 BYTE)

        private static readonly HashSet<string> Blocked = new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".dll", ".bat", ".cmd", ".com", ".msi", ".ps1", ".vbs", ".js", ".jse", ".wsf", ".scr", ".jar",
            ".asp", ".aspx", ".ashx", ".asmx", ".cshtml", ".config", ".htm", ".html", ".svg", ".php", ".sh"
        };

        private readonly string? _folder;
        private readonly ILogger<ServiceAttachmentStore> _logger;
        private readonly FileExtensionContentTypeProvider _types = new();

        public ServiceAttachmentStore(IConfiguration config, ILogger<ServiceAttachmentStore> logger)
        {
            _logger = logger;
            string? folder = config["Service:AttachmentFolder"];
            _folder = string.IsNullOrWhiteSpace(folder) ? null : Path.GetFullPath(folder.Trim());
        }

        public bool Enabled => _folder != null;

        /// <summary>Saves the upload as "{requestId}_{file name}" (old YA name). Returns the stored file name, or an error text.</summary>
        public async Task<(string? FileName, string? Error)> SaveAsync(IFormFile file, long requestId)
        {
            if (_folder == null) return (null, "Attachments are not configured on this server.");
            if (file.Length <= 0) return (null, "The attached file is empty.");
            if (file.Length > MaxBytes) return (null, "The attached file is too big (maximum 20 MB).");

            string original = Path.GetFileName(file.FileName ?? "");
            foreach (char c in Path.GetInvalidFileNameChars()) original = original.Replace(c, '_');
            string ext = Path.GetExtension(original);
            if (string.IsNullOrWhiteSpace(original) || string.IsNullOrEmpty(ext)) return (null, "The attached file has no file type.");
            if (Blocked.Contains(ext)) return (null, $"Files of type {ext} are not allowed.");
            if (!Directory.Exists(_folder)) return (null, "The attachment folder cannot be reached. Please contact Admin.");

            string baseName = Path.GetFileNameWithoutExtension(original);
            string prefix = requestId + "_";

            // never overwrite an existing file: "name.ext", "name(1).ext", ...
            for (int n = 0; n < 1000; n++)
            {
                string suffix = n == 0 ? "" : $"({n})";
                string body = baseName;
                string name = prefix + body + suffix + ext;
                while (Encoding.UTF8.GetByteCount(name) > MaxNameBytes && body.Length > 1)
                {
                    body = body[..^1];
                    name = prefix + body + suffix + ext;
                }
                if (Encoding.UTF8.GetByteCount(name) > MaxNameBytes) return (null, "The file name is too long.");

                string full = Path.Combine(_folder, name);
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
                    _logger.LogError(ex, "Service attachment could not be saved. RequestId={RequestId}", requestId);
                    return (null, "The file could not be saved. Please try again.");
                }
            }
            return (null, "The file could not be saved (too many files with the same name).");
        }

        /// <summary>Full path + content type of a stored attachment, or null when it does not exist in the folder.</summary>
        public (string Path, string ContentType)? Find(string? storedName)
        {
            if (_folder == null || string.IsNullOrWhiteSpace(storedName)) return null;
            string name = Path.GetFileName(storedName.Trim());                      // no folder parts ("..\" etc.)
            string full = Path.GetFullPath(Path.Combine(_folder, name));
            if (!full.StartsWith(_folder, StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) return null;
            return (full, _types.TryGetContentType(name, out var t) ? t : "application/octet-stream");
        }
    }
}
