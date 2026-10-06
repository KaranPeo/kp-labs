using Amazon.S3;
using Amazon.S3.Model;
using Npgsql;

namespace MovementAnalysis.Api;

internal static class VideoEndpoints
{
    public static void Map(WebApplication app)
    {
        var isDevelopment = app.Environment.IsDevelopment();

        // Create an awaiting_upload row and return a signed PUT URL for MinIO.
        app.MapPost("/videos/upload-url", async (
            UploadUrlRequest body,
            NpgsqlDataSource dataSource,
            PresignS3 presign,
            StorageBucket storage,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.FileName))
            {
                return Results.BadRequest(new { error = "fileName is required" });
            }

            // Drop client-supplied directories ("../x.mov" -> "x.mov").
            var fileName = Path.GetFileName(body.FileName);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return Results.BadRequest(new { error = "fileName is required" });
            }

            var movementType = string.IsNullOrWhiteSpace(body.MovementType) ? "squat" : body.MovementType;
            // Server-generated before the file exists: the row, the object key, and the signed URL all
            // share this id, so the webhook can match the upload back, and clients can't pick or collide ids.
            var videoId = Guid.NewGuid();
            var userId = Guid.NewGuid(); // placeholder until auth exists
            var storageKey = $"{videoId}/{fileName}"; // webhook looks the row up by this key

            // Signed locally, no network call. Valid for one key, PUT only, 15 minutes.
            // HTTP because local MinIO has no TLS (the SDK defaults to https).
            var url = presign.Client.GetPreSignedURL(new GetPreSignedUrlRequest
            {
                BucketName = storage.Name,
                Key = storageKey,
                Verb = HttpVerb.PUT,
                Expires = DateTime.UtcNow.AddMinutes(15),
                Protocol = Protocol.HTTP,
            });

            await using var conn = await dataSource.OpenConnectionAsync(ct);
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO uploads (id, user_id, movement_type, video_object_key, status)
                VALUES (@id, @userId, @movementType::movement_type, @videoObjectKey, 'awaiting_upload'::upload_status)
                """,
                conn);
            cmd.Parameters.AddWithValue("id", videoId);
            cmd.Parameters.AddWithValue("userId", userId);
            cmd.Parameters.AddWithValue("movementType", movementType);
            cmd.Parameters.AddWithValue("videoObjectKey", storageKey);
            await cmd.ExecuteNonQueryAsync(ct);

            if (isDevelopment)
            {
                var uploadCommand = $"curl -X PUT -T '{fileName}' \"{url}\"";
                return Results.Ok(new { videoId, presignedUrl = url, uploadCommand });
            }

            return Results.Ok(new { videoId, presignedUrl = url });
        });

        // Read an upload's status (polled by the upload page).
        app.MapGet("/videos/{id:guid}", GetVideo);
    }

    private static async Task<IResult> GetVideo(
        Guid id,
        NpgsqlDataSource dataSource,
        PresignS3 presign,
        StorageBucket storage,
        CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT id, user_id, movement_type::text, video_object_key, status::text, created_at, updated_at
            FROM uploads
            WHERE id = @id
            """,
            conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return Results.NotFound();
        }

        var storageKey = reader.GetString(3);
        var status = reader.GetString(4);
        string? videoUrl = null;
        if (status == "done")
        {
            videoUrl = presign.Client.GetPreSignedURL(new GetPreSignedUrlRequest
            {
                BucketName = storage.Name,
                Key = storageKey,
                Verb = HttpVerb.GET,
                Expires = DateTime.UtcNow.AddMinutes(15),
                Protocol = Protocol.HTTP,
            });
        }

        return Results.Ok(new UploadRow(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            storageKey,
            status,
            reader.GetFieldValue<DateTimeOffset>(5),
            reader.GetFieldValue<DateTimeOffset>(6),
            videoUrl));
    }
}
