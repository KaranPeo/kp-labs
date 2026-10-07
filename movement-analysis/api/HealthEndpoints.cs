using Amazon.S3;
using Npgsql;

namespace MovementAnalysis.Api;

internal static class HealthEndpoints
{
    public static void Map(WebApplication app)
    {
        // 200 if Postgres and MinIO respond, else 503. Kafka is not checked.
        app.MapGet("/health", async (NpgsqlDataSource dataSource, IAmazonS3 s3, CancellationToken ct) =>
        {
            var databaseOk = await CheckDb(dataSource, ct);
            var storageOk = await CheckStorage(s3, ct);
            var ok = databaseOk && storageOk;
            var payload = new
            {
                status = ok ? "ok" : "degraded",
                database = databaseOk ? "ok" : "unavailable",
                storage = storageOk ? "ok" : "unavailable",
            };
            return ok ? Results.Ok(payload) : Results.Json(payload, statusCode: 503);
        });
    }

    private static async Task<bool> CheckDb(NpgsqlDataSource dataSource, CancellationToken ct)
    {
        try
        {
            await using var conn = await dataSource.OpenConnectionAsync(ct);
            await using var cmd = new NpgsqlCommand("SELECT 1", conn);
            await cmd.ExecuteScalarAsync(ct);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static async Task<bool> CheckStorage(IAmazonS3 s3, CancellationToken ct)
    {
        try
        {
            await s3.ListBucketsAsync(ct);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
