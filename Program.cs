using IndexerCore.Protocol.Messages;
using IndexerCore.Data;
using IndexerCore.Monero;
using IndexerCore.Services;
using IndexerCore.Protocol;
using IndexerCore.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.RateLimiting;
using IndexerCore.Services.Attachments;
using IndexerCore.Services.Collections;
using IndexerCore.Services.Indexing;
using IndexerCore.Services.Issuance;
using IndexerCore.Services.Sales;
using IndexerCore.Services.Items;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddConcurrencyLimiter("collections", limiter =>
    {
        limiter.PermitLimit = 8;
        limiter.QueueLimit = 0;
    });
});
builder.Services.AddOptions<MoneroOptions>()
    .BindConfiguration(MoneroOptions.SectionName)
    .ValidateDataAnnotations()
    .Validate(options => Uri.TryCreate(options.RpcUrl, UriKind.Absolute, out var uri) &&
                         uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment),
        "Monero:RpcUrl must be an absolute HTTP(S) base URL without a query or fragment.")
    .ValidateOnStart();
builder.Services.AddHttpClient<MoneroRpcClient>((services, http) =>
{
    var options = services.GetRequiredService<IOptions<MoneroOptions>>().Value;
    http.BaseAddress = new Uri(options.RpcUrl.TrimEnd('/') + "/");
    http.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
});
builder.Services.AddSingleton(new SemaphoreSlim(1, 1));
builder.Services.AddOptions<ProtocolOptions>().BindConfiguration(ProtocolOptions.SectionName);
builder.Services.AddSingleton<ProtocolConfigurationRegistry>();
builder.Services.AddScoped<ChainReorganization>();
builder.Services.AddScoped<DataChunkHandler>();
builder.Services.AddScoped<AttachmentService>();
builder.Services.AddScoped<CollectionTermsService>();
builder.Services.AddScoped<CollectionCreateHandler>();
builder.Services.AddScoped<NativeTransactionReader>();
builder.Services.AddScoped<CollectionStateService>();
builder.Services.AddScoped<CollectionControlHandler>();
builder.Services.AddScoped<IssueSplitHandler>();
builder.Services.AddScoped<PrimaryPurchaseHandler>();
builder.Services.AddScoped<IssuanceQueryService>();
builder.Services.AddScoped<CollectionQueryService>();
builder.Services.AddScoped<ItemQueryService>();
builder.Services.AddScoped<ItemIdentityBackfill>();
builder.Services.AddScoped<ItemSpendProcessor>();
builder.Services.AddScoped<MessageBatchProcessor>();
builder.Services.AddHostedService<TransactionScanner>();
builder.Services.AddHostedService<MessageProcessor>();

var connectionString = builder.Configuration.GetConnectionString("IndexerDatabase");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("ConnectionStrings:IndexerDatabase is required.");
}

builder.Services.AddDbContext<IndexerDbContext>(options =>
    options.UseNpgsql(connectionString));

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IndexerDbContext>();
    var configurations = scope.ServiceProvider.GetRequiredService<ProtocolConfigurationRegistry>();
    if (scope.ServiceProvider.GetRequiredService<IOptions<ProtocolOptions>>().Value.Configurations.Count == 0)
        app.Logger.LogInformation("no protocol configurations; collection creation will remain unsupported");
    app.Logger.LogInformation("Applying database migrations...");
    await db.Database.MigrateAsync();
    await configurations.ValidateHistoryAsync(db, CancellationToken.None);
    await scope.ServiceProvider.GetRequiredService<ItemIdentityBackfill>().RunAsync(CancellationToken.None);
    await db.Messages.Where(m => m.Status == MessageStatus.Unsupported && m.Version == XtopMessageReader.CurrentVersion &&
                                (m.Operation == 1 || m.Operation == 2 || m.Operation == 0x0D || m.Operation == 0x0E || m.Operation == 0x0F || m.Operation == 0x12 || m.Operation == 9 || m.Operation == 0x13))
        .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.Status, MessageStatus.Pending).SetProperty(m => m.Error, (string?)null));
    app.Logger.LogInformation("Database is up to date.");
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    await next(context);
});
app.UseRateLimiter();
app.MapControllers();
app.Run();
