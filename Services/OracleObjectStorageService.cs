using Amazon.S3;
using Amazon.S3.Model;

namespace back_mylife.Services
{
    public class OracleObjectStorageService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<OracleObjectStorageService> _logger;
        private readonly string _accessKey;
        private readonly string _secretKey;
        private readonly string _region;
        private readonly string _namespace;
        private readonly string _bucketName;
        private readonly string _publicUrlBase;

        public OracleObjectStorageService(IConfiguration configuration, ILogger<OracleObjectStorageService> logger)
        {
            _configuration = configuration;
            _logger = logger;

            _accessKey = Environment.GetEnvironmentVariable("OCI_S3_ACCESS_KEY") 
                ?? Environment.GetEnvironmentVariable("OCI_ACCESS_KEY") 
                ?? _configuration["OCI:AccessKey"] 
                ?? string.Empty;

            _secretKey = Environment.GetEnvironmentVariable("OCI_S3_SECRET_KEY") 
                ?? Environment.GetEnvironmentVariable("OCI_SECRET_KEY") 
                ?? _configuration["OCI:SecretKey"] 
                ?? string.Empty;

            _region = Environment.GetEnvironmentVariable("OCI_REGION") 
                ?? _configuration["OCI:Region"] 
                ?? "ap-singapore-1";

            _namespace = Environment.GetEnvironmentVariable("OCI_NAMESPACE") 
                ?? _configuration["OCI:Namespace"] 
                ?? string.Empty;

            _bucketName = Environment.GetEnvironmentVariable("OCI_BUCKET_NAME") 
                ?? _configuration["OCI:BucketName"] 
                ?? "mylife-profile-bucket";

            _publicUrlBase = Environment.GetEnvironmentVariable("OCI_PUBLIC_URL_BASE") 
                ?? _configuration["OCI:PublicUrlBase"] 
                ?? string.Empty;
        }

        public bool IsConfigured => 
            !string.IsNullOrWhiteSpace(_accessKey) && 
            !string.IsNullOrWhiteSpace(_secretKey) && 
            !string.IsNullOrWhiteSpace(_namespace) && 
            !string.IsNullOrWhiteSpace(_bucketName);

        private IAmazonS3 CreateS3Client()
        {
            if (!IsConfigured)
            {
                throw new InvalidOperationException("Oracle Cloud Object Storage is not configured. Please check OCI environment variables.");
            }

            var endpoint = $"https://{_namespace.Trim()}.compat.objectstorage.{_region.Trim()}.oraclecloud.com";

            var config = new AmazonS3Config
            {
                ServiceURL = endpoint,
                ForcePathStyle = true,
                AuthenticationRegion = _region.Trim(),
                UseAccelerateEndpoint = false
            };

            return new AmazonS3Client(_accessKey.Trim(), _secretKey.Trim(), config);
        }

        public async Task<(bool Success, string? Url, string? ErrorMessage)> UploadProfileImageAsync(
            Guid userId, 
            Stream fileStream, 
            string contentType, 
            string extension, 
            string? oldImageUrl = null)
        {
            try
            {
                if (!IsConfigured)
                {
                    return (false, null, "Oracle Cloud Object Storage ยังไม่ได้กำหนดค่า (OCI_S3_ACCESS_KEY, OCI_S3_SECRET_KEY, OCI_NAMESPACE, OCI_BUCKET_NAME)");
                }

                using var client = CreateS3Client();

                // Buffer stream to memory to ensure known length and prevent chunked streaming issues with OCI S3 API
                using var memoryStream = new MemoryStream();
                await fileStream.CopyToAsync(memoryStream);
                memoryStream.Position = 0;

                // Format: profiles/{userId}-{timestamp}.ext
                var normalizedExt = extension.StartsWith('.') ? extension : $".{extension}";
                var objectKey = $"profiles/{userId}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{normalizedExt}";

                var putRequest = new PutObjectRequest
                {
                    BucketName = _bucketName.Trim(),
                    Key = objectKey,
                    InputStream = memoryStream,
                    ContentType = contentType,
                    UseChunkEncoding = false,
                    DisablePayloadSigning = true
                };

                var response = await client.PutObjectAsync(putRequest);

                if (response.HttpStatusCode != System.Net.HttpStatusCode.OK && 
                    response.HttpStatusCode != System.Net.HttpStatusCode.Created && 
                    response.HttpStatusCode != System.Net.HttpStatusCode.Accepted)
                {
                    _logger.LogWarning("OCI Object Storage upload returned non-success status: {StatusCode}", response.HttpStatusCode);
                }

                // Construct public URL
                string publicUrl;
                if (!string.IsNullOrWhiteSpace(_publicUrlBase))
                {
                    publicUrl = $"{_publicUrlBase.TrimEnd('/')}/{objectKey}";
                }
                else
                {
                    publicUrl = $"https://objectstorage.{_region.Trim()}.oraclecloud.com/n/{_namespace.Trim()}/b/{_bucketName.Trim()}/o/{objectKey}";
                }

                // Clean up previous image if exists
                if (!string.IsNullOrWhiteSpace(oldImageUrl))
                {
                    _ = DeleteProfileImageAsync(oldImageUrl).ConfigureAwait(false);
                }

                return (true, publicUrl, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload profile image to Oracle Cloud Object Storage for User {UserId}", userId);
                return (false, null, $"อัปโหลดรูปภาพไปยัง Oracle Cloud ไม่สำเร็จ: {ex.Message}");
            }
        }

        public async Task<bool> DeleteProfileImageAsync(string? imageUrlOrKey)
        {
            if (string.IsNullOrWhiteSpace(imageUrlOrKey) || !IsConfigured)
            {
                return false;
            }

            try
            {
                var objectKey = ExtractObjectKey(imageUrlOrKey);
                if (string.IsNullOrWhiteSpace(objectKey))
                {
                    return false;
                }

                using var client = CreateS3Client();
                var deleteRequest = new DeleteObjectRequest
                {
                    BucketName = _bucketName.Trim(),
                    Key = objectKey
                };

                await client.DeleteObjectAsync(deleteRequest);
                _logger.LogInformation("Deleted profile image object: {ObjectKey}", objectKey);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete profile image from OCI: {ImageUrl}", imageUrlOrKey);
                return false;
            }
        }

        public async Task<(Stream? Stream, string ContentType)> GetObjectStreamAsync(string objectKey)
        {
            if (!IsConfigured || string.IsNullOrWhiteSpace(objectKey))
            {
                return (null, "application/octet-stream");
            }

            try
            {
                using var client = CreateS3Client();
                var getRequest = new GetObjectRequest
                {
                    BucketName = _bucketName.Trim(),
                    Key = objectKey
                };

                var response = await client.GetObjectAsync(getRequest);
                var memoryStream = new MemoryStream();
                await response.ResponseStream.CopyToAsync(memoryStream);
                memoryStream.Position = 0;

                return (memoryStream, response.Headers.ContentType ?? "image/jpeg");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch object stream for key {ObjectKey}", objectKey);
                return (null, "application/octet-stream");
            }
        }

        public string ExtractObjectKey(string imageUrlOrKey)
        {
            if (string.IsNullOrWhiteSpace(imageUrlOrKey)) return string.Empty;

            var trimmed = imageUrlOrKey.Trim();

            // If it's already an object key like profiles/xxx.jpg
            if (trimmed.StartsWith("profiles/", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            // If it's a URL like https://objectstorage.../o/profiles/xxx.jpg
            var oIndex = trimmed.IndexOf("/o/", StringComparison.OrdinalIgnoreCase);
            if (oIndex >= 0)
            {
                var sub = trimmed.Substring(oIndex + 3);
                return Uri.UnescapeDataString(sub);
            }

            // Fallback: check if /profiles/ is in the URL
            var profilesIndex = trimmed.IndexOf("/profiles/", StringComparison.OrdinalIgnoreCase);
            if (profilesIndex >= 0)
            {
                var sub = trimmed.Substring(profilesIndex + 1);
                return Uri.UnescapeDataString(sub);
            }

            return trimmed;
        }
    }
}

