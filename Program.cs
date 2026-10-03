using IndexerCore.Data;
using IndexerCore.Monero;
using IndexerCore.Services;
using IndexerCore.Protocol;
using IndexerCore.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
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
    await db.Messages.Where(m => m.Status == MessageStatus.Unsupported && m.Version == XtopMessageReader.CurrentVersion &&
                                (m.Operation == 1 || m.Operation == 2))
        .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.Status, MessageStatus.Pending).SetProperty(m => m.Error, (string?)null));
    app.Logger.LogInformation("Database is up to date.");
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.Run();
