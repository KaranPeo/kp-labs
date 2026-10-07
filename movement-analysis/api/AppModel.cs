using Amazon.S3;
using Amazon.S3.Model;

namespace MovementAnalysis.Api;

internal sealed record UploadUrlRequest(string FileName, string? MovementType);

// Signed locally, no network call. Valid for one key and one verb, 15 minutes.
// HTTP because local MinIO has no TLS (the SDK defaults to https).
internal sealed record Presigner(IAmazonS3 Client, string Bucket)
{
    public string Url(string key, HttpVerb verb) => Client.GetPreSignedURL(new GetPreSignedUrlRequest
    {
        BucketName = Bucket,
        Key = key,
        Verb = verb,
        Expires = DateTime.UtcNow.AddMinutes(15),
        Protocol = Protocol.HTTP,
    });
}

internal sealed record KafkaTopic(string Name);

internal sealed record UploadRow(
    Guid Id,
    Guid UserId,
    string MovementType,
    string VideoObjectKey,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? VideoUrl); // signed GET URL, only once status is done

internal sealed class MinioNotification
{
    public List<MinioRecord>? Records { get; set; }
}

internal sealed class MinioRecord
{
    public MinioS3? S3 { get; set; }
}

internal sealed class MinioS3
{
    public MinioObject? Object { get; set; }
}

internal sealed class MinioObject
{
    public string? Key { get; set; }
}
