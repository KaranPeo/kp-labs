using Amazon.S3;

namespace MovementAnalysis.Api;

internal sealed record UploadUrlRequest(string FileName, string? MovementType);

internal sealed record PresignS3(IAmazonS3 Client);

internal sealed record StorageBucket(string Name);

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
