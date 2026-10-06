using System.Text.Json;
using Confluent.Kafka;
using Npgsql;

namespace MovementAnalysis.Api;

internal static class MinioWebhookEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    // MinIO calls this after each object PUT (wired up in docker-compose.yml).
    public static void Map(WebApplication app)
    {
        app.MapPost("/webhooks/minio/object-created", Handle).DisableAntiforgery();
    }

    // Ignored events (bad JSON, unknown key, already handled) still return 200.
    // Known gap: if the Kafka publish fails after the row is set to pending, no job is ever sent.
    private static async Task<IResult> Handle(
        HttpRequest request,
        NpgsqlDataSource dataSource,
        IProducer<string, string> producer,
        KafkaTopic topic,
        CancellationToken ct)
    {
        using var reader = new StreamReader(request.Body);
        var raw = await reader.ReadToEndAsync(ct);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Results.Ok();
        }

        MinioNotification? notification;
        try
        {
            notification = JsonSerializer.Deserialize<MinioNotification>(raw, JsonOptions);
        }
        catch (JsonException)
        {
            return Results.Ok();
        }

        // S3 event format: Records[0].s3.object.key (one record per object).
        var encodedKey = notification?.Records?.FirstOrDefault()?.S3?.Object?.Key;
        if (string.IsNullOrWhiteSpace(encodedKey))
        {
            return Results.Ok();
        }

        // Keys arrive URL-encoded ("+" means space).
        var storageKey = Uri.UnescapeDataString(encodedKey.Replace("+", "%20"));

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var lookup = new NpgsqlCommand(
            """
            SELECT id, movement_type::text, video_object_key, status::text
            FROM uploads
            WHERE video_object_key = @key
            """,
            conn);
        lookup.Parameters.AddWithValue("key", storageKey);
        await using var rows = await lookup.ExecuteReaderAsync(ct);
        if (!await rows.ReadAsync(ct))
        {
            // Not uploaded via POST /videos/upload-url.
            return Results.Ok();
        }

        var videoId = rows.GetGuid(0);
        var exerciseType = rows.GetString(1);
        var status = rows.GetString(3);
        await rows.CloseAsync();

        // Already handled (duplicate event or re-upload).
        if (status != "awaiting_upload")
        {
            return Results.Ok();
        }

        // Status check in WHERE: if two deliveries race, only one updates the row and publishes.
        await using var update = new NpgsqlCommand(
            """
            UPDATE uploads
            SET status = 'pending'::upload_status, updated_at = now()
            WHERE id = @id AND status = 'awaiting_upload'::upload_status
            """,
            conn);
        update.Parameters.AddWithValue("id", videoId);
        var updated = await update.ExecuteNonQueryAsync(ct);
        if (updated == 0)
        {
            return Results.Ok();
        }

        // Shape consumed by worker/main.py.
        var job = JsonSerializer.Serialize(new
        {
            videoId,
            storageKey,
            exerciseType,
        });
        await producer.ProduceAsync(
            topic.Name,
            new Message<string, string> { Key = videoId.ToString(), Value = job },
            ct);

        return Results.Ok();
    }
}
