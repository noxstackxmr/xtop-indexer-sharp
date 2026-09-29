using IndexerCore.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();

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
    app.Logger.LogInformation("Applying database migrations...");
    await db.Database.MigrateAsync();
    app.Logger.LogInformation("Database is up to date.");
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.Run();
