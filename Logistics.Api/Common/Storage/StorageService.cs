using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;

namespace Logistics.Api.Common.Storage;

public interface IStorageService
{
    Task<string> UploadFileAsync(Stream fileStream, string fileName, string contentType);
}

public class MinioStorageService : IStorageService
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;
    private readonly string _endpoint;

    public MinioStorageService(IAmazonS3 s3Client, IConfiguration configuration)
    {
        _s3Client = s3Client;
        _bucketName = configuration["Minio:BucketName"] ?? "logistics-pod";
        _endpoint = configuration["Minio:Endpoint"] ?? "http://localhost:9000";
    }

    public async Task<string> UploadFileAsync(Stream fileStream, string fileName, string contentType)
    {
        // Pastikan bucket sudah ada
        var bucketExists = await AmazonS3Util.DoesS3BucketExistV2Async(_s3Client, _bucketName);
        if (!bucketExists)
        {
            await _s3Client.PutBucketAsync(new PutBucketRequest { BucketName = _bucketName });
        }

        var uniqueFileName = $"{Guid.NewGuid()}_{fileName}";

        var putRequest = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = uniqueFileName,
            InputStream = fileStream,
            ContentType = contentType
        };

        await _s3Client.PutObjectAsync(putRequest);

        // Mengembalikan URL publik file di MinIO
        return $"{_endpoint}/{_bucketName}/{uniqueFileName}";
    }
}