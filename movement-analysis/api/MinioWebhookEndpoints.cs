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

        // Status check in WHERE: unknown keys and already-handled rows (duplicate events,
        // re-uploads) match nothing, and if two deliveries race only one updates and publishes.
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var update = new NpgsqlCommand(
            """
            UPDATE uploads
            SET status = 'pending'::upload_status, updated_at = now()
            WHERE video_object_key = @key AND status = 'awaiting_upload'::upload_status
            RETURNING id, movement_type::text
            """,
            conn);
        update.Parameters.AddWithValue("key", storageKey);
        await using var row = await update.ExecuteReaderAsync(ct);
        if (!await row.ReadAsync(ct))
        {
            return Results.Ok();
        }

        var videoId = row.GetGuid(0);
        var exerciseType = row.GetString(1);

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
