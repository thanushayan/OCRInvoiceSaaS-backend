using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OcrInvoiceSaaS.Libs;

namespace OcrInvoiceSaaS.Controllers;

/// <summary>
/// Stub file upload controller. Currently returns a pre-signed URL placeholder.
///
/// When you're ready to add real S3 uploads:
///   1. Install AWSSDK.S3
///   2. Inject IAmazonS3 and implement GeneratePresignedUrlAsync
///   3. Replace the placeholder response below
/// </summary>
[Authorize]
[ApiController]
[Route("api/files")]
public class FileUploadController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly CurrentUserProvider _currentUser;
    private readonly ILogger<FileUploadController> _logger;

    private static readonly HashSet<string> AllowedTypes =
        new(StringComparer.OrdinalIgnoreCase) { "application/pdf", "image/png", "image/jpeg", "image/jpg" };

    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".png", ".jpg", ".jpeg" };

    public FileUploadController(
        IConfiguration config,
        CurrentUserProvider currentUser,
        ILogger<FileUploadController> logger)
    {
        _config = config;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Request a pre-signed upload URL. Client uploads directly to S3.
    /// Returns the URL to use for the invoice FileUrl field after upload.
    /// </summary>
    [HttpPost("presign")]
    public IActionResult GetPresignedUploadUrl([FromBody] PresignRequest request)
    {
        var ext = Path.GetExtension(request.FileName)?.ToLowerInvariant();

        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
            return BadRequest(new { error = "File type not allowed. Use PDF, PNG or JPEG." });

        var userId = _currentUser.GetUserId();
        var objectKey = $"invoices/{userId}/{Guid.NewGuid()}{ext}";

        // TODO: Replace with real AWS pre-signed URL generation:
        //
        // var s3Client = HttpContext.RequestServices.GetRequiredService<IAmazonS3>();
        // var bucket = _config["Aws:BucketName"]!;
        // var urlRequest = new GetPreSignedUrlRequest
        // {
        //     BucketName = bucket,
        //     Key = objectKey,
        //     Verb = HttpVerb.PUT,
        //     Expires = DateTime.UtcNow.AddMinutes(10),
        //     ContentType = request.ContentType
        // };
        // var presignedUrl = s3Client.GetPreSignedURL(urlRequest);
        // var fileUrl = $"https://{bucket}.s3.amazonaws.com/{objectKey}";

        var placeholderUploadUrl = $"https://your-bucket.s3.amazonaws.com/{objectKey}?X-Amz-Signature=PLACEHOLDER";
        var fileUrl = $"https://your-bucket.s3.amazonaws.com/{objectKey}";

        _logger.LogInformation("Presign requested for {FileName} by user {UserId}", request.FileName, userId);

        return Ok(new PresignResponse
        {
            UploadUrl = placeholderUploadUrl,
            FileUrl = fileUrl,
            ObjectKey = objectKey,
            ExpiresInSeconds = 600
        });
    }

    /// <summary>
    /// Direct file upload (multipart/form-data). Max 10 MB.
    /// Use this instead of pre-signed URLs for simpler integrations.
    /// In production this should stream directly to S3, not buffer in memory.
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10 MB
    public async Task<IActionResult> Upload(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file provided." });

        if (!AllowedTypes.Contains(file.ContentType))
            return BadRequest(new { error = "File type not allowed. Use PDF, PNG or JPEG." });

        var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant();
        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
            return BadRequest(new { error = "File extension not allowed." });

        var userId = _currentUser.GetUserId();
        var objectKey = $"invoices/{userId}/{Guid.NewGuid()}{ext}";

        // TODO: Stream file.OpenReadStream() directly to S3:
        //
        // var s3Client = HttpContext.RequestServices.GetRequiredService<IAmazonS3>();
        // await s3Client.PutObjectAsync(new PutObjectRequest
        // {
        //     BucketName = _config["Aws:BucketName"],
        //     Key = objectKey,
        //     InputStream = file.OpenReadStream(),
        //     ContentType = file.ContentType
        // });

        // Placeholder: in dev, you could save to wwwroot/uploads instead
        _logger.LogInformation("File upload placeholder: {FileName} ({Size} bytes)", file.FileName, file.Length);

        var fileUrl = $"https://your-bucket.s3.amazonaws.com/{objectKey}";

        return Ok(new UploadResponse
        {
            FileUrl = fileUrl,
            FileName = file.FileName,
            FileType = ext.TrimStart('.'),
            SizeBytes = file.Length
        });
    }
}

public class PresignRequest
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
}

public class PresignResponse
{
    public string UploadUrl { get; set; } = string.Empty;
    public string FileUrl { get; set; } = string.Empty;
    public string ObjectKey { get; set; } = string.Empty;
    public int ExpiresInSeconds { get; set; }
}

public class UploadResponse
{
    public string FileUrl { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}
