using Azure.Identity;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Endpoints;
using Scmos.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Secrets — the connection string, the OpenAI key, the proxy key — live in Key
// Vault and are read with the App Service managed identity. Locally there is no
// vault and no identity, so the section is simply absent and user-secrets or
// appsettings.Development.json answer instead.
var vault = builder.Configuration["KeyVault:Uri"];
if (!string.IsNullOrWhiteSpace(vault))
{
    builder.Configuration.AddAzureKeyVault(new Uri(vault), new DefaultAzureCredential());
}

builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.Section));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.Section));
builder.Services.Configure<OpenAiOptions>(builder.Configuration.GetSection(OpenAiOptions.Section));

var connectionString = builder.Configuration.GetConnectionString("ScmosDb") ?? "";
builder.Services.AddDbContext<ScmosDbContext>(options =>
    options.UseSqlServer(connectionString, sql =>
    {
        // Azure SQL drops idle and throttled connections as a matter of course;
        // without this a save fails rather than waiting a second and landing.
        sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
        sql.CommandTimeout(120);
    }));

builder.Services.AddScoped<JobsRepository>();
builder.Services.AddScoped<KpiService>();
builder.Services.AddScoped<KpiEngine>();
builder.Services.AddScoped<WorkflowService>();
builder.Services.AddScoped<PreRunService>();
builder.Services.AddScoped<MonitoringService>();
builder.Services.AddScoped<RateService>();
builder.Services.AddScoped<SupplierService>();
builder.Services.AddScoped<IncidentService>();
builder.Services.AddScoped<AiGateway>();
builder.Services.Configure<PreRunOptions>(builder.Configuration.GetSection(PreRunOptions.Section));
builder.Services.AddSingleton<IUserAccessor, UserAccessor>();
builder.Services.AddSingleton<IFileStore, BlobFileStore>();
builder.Services.AddSingleton<IDocumentExtractor, DocumentExtractor>();

// Only when there is somewhere to send it. Registering the exporter without a
// connection string throws during host start, which would take the whole API
// down on any environment that has not been given one — including the seed and
// migrate commands, which need no telemetry at all.
var telemetry = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]
                ?? builder.Configuration["ApplicationInsights:ConnectionString"];
if (!string.IsNullOrWhiteSpace(telemetry))
{
    builder.Services.AddApplicationInsightsTelemetry(options => options.ConnectionString = telemetry);
}

builder.Services.AddHealthChecks().AddDbContextCheck<ScmosDbContext>("database");
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

// The deployed shape puts the web app's proxy in front, same site, so no CORS is
// involved. An origin is only configured when a browser calls this API directly.
var allowedOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
if (allowedOrigins.Length > 0)
{
    builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
}

var app = builder.Build();

// `dotnet run -- --seed ../../public/data/ops.json` keys the delivered plan into
// an empty register. It is idempotent on the job key, so running it twice is
// the same as running it once.
if (args.Contains("--seed"))
{
    return await PlanSeeder.RunAsync(app, args);
}

if (args.Contains("--seed-suppliers"))
{
    return await SupplierSeeder.RunAsync(app, args);
}

if (args.Contains("--migrate-status"))
{
    return await StatusMigration.RunAsync(app, args);
}

if (args.Contains("--migrate"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<ScmosDbContext>().Database.MigrateAsync();
    app.Logger.LogInformation("Database is up to date.");
    return 0;
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();
if (allowedOrigins.Length > 0) app.UseCors();

app.MapHealthChecks("/health");
app.MapMe();
app.MapJobs();
app.MapKpi();
app.MapWorkflow();
app.MapPreRun();
app.MapMonitoring();
app.MapSuppliers();
app.MapUploads();
app.MapOperations();
app.MapAiExtract();

await app.RunAsync();
return 0;
