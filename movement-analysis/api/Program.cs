using Npgsql;

DotNetEnv.Env.NoClobber().TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

string? Config(string name) => builder.Configuration[name];
string Required(string name) =>
    Config(name)
    ?? throw new InvalidOperationException($"{name} is required. Copy .env.example to .env at the repo root.");

var host = Config("POSTGRES_HOST") ?? "127.0.0.1";
var port = Config("POSTGRES_PORT") ?? "5433";
var database = Required("POSTGRES_DB");
var user = Required("POSTGRES_USER");
var password = Required("POSTGRES_PASSWORD");
var connectionString =
    $"Host={host};Port={port};Database={database};Username={user};Password={password}";

builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapOpenApi();
app.UseSwaggerUI(o => o.SwaggerEndpoint("/openapi/v1.json", "v1"));
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.MapGet("/health", async (NpgsqlDataSource dataSource, CancellationToken ct) =>
{
    try
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("SELECT 1", conn);
        await cmd.ExecuteScalarAsync(ct);
        return Results.Ok(new { status = "ok", database = "ok" });
    }
    catch (Exception)
    {
        return Results.Json(new { status = "ok", database = "unavailable" }, statusCode: 503);
    }
});

app.MapPost("/uploads", async (NpgsqlDataSource dataSource, CancellationToken ct) =>
{
    var id = Guid.NewGuid();
    var userId = Guid.NewGuid();
    const string movementType = "squat";
    var videoObjectKey = $"dummy/{id}.mp4";
    const string status = "uploaded";

    await using var conn = await dataSource.OpenConnectionAsync(ct);
    await using var cmd = new NpgsqlCommand(
        """
        INSERT INTO uploads (id, user_id, movement_type, video_object_key, status)
        VALUES (@id, @userId, @movementType::movement_type, @videoObjectKey, @status::upload_status)
        RETURNING id, user_id, movement_type::text, video_object_key, status::text, created_at, updated_at
        """,
        conn);
    cmd.Parameters.AddWithValue("id", id);
    cmd.Parameters.AddWithValue("userId", userId);
    cmd.Parameters.AddWithValue("movementType", movementType);
    cmd.Parameters.AddWithValue("videoObjectKey", videoObjectKey);
    cmd.Parameters.AddWithValue("status", status);

    await using var reader = await cmd.ExecuteReaderAsync(ct);
    await reader.ReadAsync(ct);
    var row = ReadUpload(reader);
    return Results.Created($"/uploads/{id}", row);
});

app.MapGet("/uploads/{id:guid}", async (Guid id, NpgsqlDataSource dataSource, CancellationToken ct) =>
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

    return Results.Ok(ReadUpload(reader));
});

app.Run();

static UploadRow ReadUpload(NpgsqlDataReader reader) => new(
    reader.GetGuid(0),
    reader.GetGuid(1),
    reader.GetString(2),
    reader.GetString(3),
    reader.GetString(4),
    reader.GetFieldValue<DateTimeOffset>(5),
    reader.GetFieldValue<DateTimeOffset>(6));

internal sealed record UploadRow(
    Guid Id,
    Guid UserId,
    string MovementType,
    string VideoObjectKey,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
