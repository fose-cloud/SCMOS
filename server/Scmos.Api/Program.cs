using System.IO.Compression;
using Azure.Identity;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Endpoints;
using Scmos.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// A machine-local override that is never committed — the storage emulator's
// connection string, a real database to point at for an afternoon. It was
// already git-ignored and simply never loaded, which meant the file people were
// told to create did nothing.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

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
        // -2 is the command timeout raised while a serverless database resumes.
        sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: [-2]);
        sql.CommandTimeout(120);
    }));

builder.Services.AddMemoryCache();
builder.Services.AddScoped<JobRegisterCache>();
builder.Services.AddScoped<JobsRepository>();
builder.Services.AddScoped<CarrierDirectory>();
builder.Services.AddScoped<WorkspaceService>();
builder.Services.AddScoped<KpiService>();
builder.Services.AddScoped<KpiEngine>();
builder.Services.AddScoped<WorkflowService>();
builder.Services.AddScoped<PreRunService>();
builder.Services.AddScoped<MonitoringService>();
builder.Services.AddScoped<RateService>();
builder.Services.AddScoped<CustomerDocumentService>();
builder.Services.AddScoped<SupplierService>();
builder.Services.AddScoped<IncidentService>();
builder.Services.AddScoped<DocumentService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<RiskService>();
builder.Services.AddScoped<CapacityService>();
builder.Services.AddScoped<CarrierService>();
builder.Services.AddScoped<TrainingService>();
builder.Services.AddScoped<DelegationService>();
builder.Services.AddScoped<OperationalIssueService>();
builder.Services.AddScoped<RotationService>();
builder.Services.AddScoped<RateInquiryService>();
builder.Services.AddScoped<VerificationService>();
builder.Services.AddScoped<AiGateway>();
// The audit trail records the caller's address and session, which only the
// request knows about.
builder.Services.AddHttpContextAccessor();
builder.Services.Configure<PreRunOptions>(builder.Configuration.GetSection(PreRunOptions.Section));
builder.Services.AddScoped<IUserAccessor, UserAccessor>();
builder.Services.AddScoped<StaffService>();
builder.Services.AddHttpClient("graph");
builder.Services.AddScoped<SignInAccountService>();
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

// The register goes out as one JSON document of every job — 2.6 MB of it, and
// it was leaving uncompressed because ASP.NET Core does not compress anything
// unless told to. JSON of this shape is enormously repetitive (the same forty
// field names on two thousand rows), so this is close to a tenfold reduction
// for one registration, and it is felt hardest by exactly the people who were
// complaining: a phone on mobile data.
//
// `EnableForHttps` is required — the default is to compress only plain HTTP,
// which in practice means never, since every request here arrives over TLS.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    // Gzip only, and measured rather than assumed. Brotli is the better
    // algorithm in general, but .NET offers it at quality 1 or quality 11 and
    // nothing between: quality 1 left this payload at 608 KB where gzip's fast
    // level reached 326 KB, and quality 11 would spend seconds of CPU per
    // request on a B1 instance. Browsers prefer brotli when it is offered, so
    // registering it here would have handed every one of them the worse of the
    // two.
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/json"]);
});
builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);

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
// Arithmetic only: no database, no register, nothing written. It answers the
// question a screen of straight hundreds cannot — whether the scorecard is
// counting wrongly, or there is genuinely nothing to count.
if (ScorecardCheck.Run(args)) return 0;
if (DuplicateCheck.Run(args)) return 0;
if (TypeCheck.Run(args)) return 0;

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

    // The staff directory used to be a hardcoded array. Seeding it here means an
    // existing deployment upgrades into a populated table rather than one where
    // nobody is recognised and every sign-in lands on Viewer.
    await scope.ServiceProvider.GetRequiredService<StaffService>().SeedAsync(CancellationToken.None);

    app.Logger.LogInformation("Database is up to date.");
    return 0;
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Ahead of everything that writes a body, or there is nothing left to compress.
app.UseResponseCompression();
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
app.MapDocuments();
app.MapAudit();
app.MapDashboard();
app.MapStaff();
app.MapCapacity();
app.MapCarrier();
app.MapTraining();
app.MapDelegations();
app.MapRateInquiries();
app.MapVerification();
app.MapUploads();
app.MapOperationalIssues();
app.MapRotation();
app.MapOperations();
app.MapAiExtract();
app.MapCustomerDocuments();

await app.RunAsync();
return 0;
