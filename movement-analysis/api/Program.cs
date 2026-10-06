using Amazon.S3;
using Confluent.Kafka;
using MovementAnalysis.Api;
using Npgsql;

// Upload flow: see movement-analysis/README.md.

// Repo-root .env for host runs; already-set env vars (Compose, launch.json) win.
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

// Signed URLs embed the host, so presigning uses the client-reachable endpoint, not the Docker-internal one.
var minioEndpoint = Config("MINIO_ENDPOINT") ?? "http://127.0.0.1:9000";
var minioPresignEndpoint = Config("MINIO_PRESIGN_ENDPOINT") ?? minioEndpoint;
var minioAccessKey = Required("MINIO_ROOT_USER");
var minioSecretKey = Required("MINIO_ROOT_PASSWORD");
var minioBucket = Config("MINIO_BUCKET") ?? "videos";
var kafkaBootstrap = Config("KAFKA_BOOTSTRAP_SERVERS") ?? "localhost:29092";
var kafkaTopic = Config("KAFKA_TOPIC") ?? "pose-estimation-jobs";

// MinIO via the AWS SDK: path-style URLs (host/bucket/key) and MinIO's default region.
AmazonS3Client CreateS3(string serviceUrl) => new(
    minioAccessKey,
    minioSecretKey,
    new AmazonS3Config
    {
        ServiceURL = serviceUrl,
        ForcePathStyle = true,
        AuthenticationRegion = "us-east-1",
    });

builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
builder.Services.AddSingleton<IAmazonS3>(_ => CreateS3(minioEndpoint));
builder.Services.AddSingleton(new PresignS3(CreateS3(minioPresignEndpoint)));
builder.Services.AddSingleton(new StorageBucket(minioBucket));
builder.Services.AddSingleton<IProducer<string, string>>(_ =>
    new ProducerBuilder<string, string>(new ProducerConfig
    {
        BootstrapServers = kafkaBootstrap,
        Acks = Acks.All, // ProduceAsync returns only once the broker has stored the message
    }).Build());
builder.Services.AddSingleton(new KafkaTopic(kafkaTopic));
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapOpenApi();
app.UseSwaggerUI(o => o.SwaggerEndpoint("/openapi/v1.json", "v1"));
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.UseStaticFiles();           // wwwroot/
app.MapGet("/upload", () => Results.Redirect("/upload.html")).ExcludeFromDescription(); // upload page

HealthEndpoints.Map(app);       // GET  /health
VideoEndpoints.Map(app);        // POST /videos/upload-url, GET /videos/{id}
MinioWebhookEndpoints.Map(app); // POST /webhooks/minio/object-created

app.Run();
