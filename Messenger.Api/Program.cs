using Messenger.Core.Models;
using Messenger.Api.Middleware;
using Messenger.Core.Options;
using Messenger.Infrastructure.Config;
using Messenger.Infrastructure.Data;
using Messenger.Infrastructure.Senders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Console;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// Configure logging for structured output and include scopes (so correlation id appears)
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
});
builder.Logging.AddDebug();

// Make IHttpContextAccessor available for services that may need to read the correlation id
builder.Services.AddHttpContextAccessor();

// === 1. Load settings ===
builder.Services.Configure<EmailOptions>(config.GetSection("Messaging:Email"));
builder.Services.Configure<WhatsAppOptions>(config.GetSection("Messaging:Whatsapp"));
builder.Services.Configure<FcmNotificationOptions>(config.GetSection("Messaging:FcmNotification"));

//builder.Services.AddHttpClient();

// This automatically registers CorvassSmsSender AND gives it a pre-configured HttpClient
//builder.Services.AddHttpClient<ISmsSender, CorvassSmsSender>(client =>
//{
//client.BaseAddress = new Uri("https://api.corvass.com/");
// Add default headers here if needed
//});

// === 2. Register DbContext ===
// ── Database ─────────────────────────────────────────────────────────────────
var connectionString = builder.Configuration.GetConnectionString("UlakConnection")
    ?? throw new InvalidOperationException("Connection string 'UlakConnection' not found.");

builder.Services.AddDbContext<MessengerDbContext>(opts =>
    opts.UseNpgsql(connectionString, npgsql =>
    {
        npgsql.MigrationsAssembly(typeof(MessengerDbContext).Assembly.FullName);
        npgsql.CommandTimeout(30);
    }));

// === 3. Register Services ===
builder.Services.AddMessengerInfrastructure(config);


// Add services to the container.

// === 4. Add Controllers ===
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// === 5. Add Authentication & JWT ===
// === 6. Add Authorization ===
// === 7. Swagger ===

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    scope.ServiceProvider
         .GetRequiredService<MessengerDbContext>()
         .Database.Migrate();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

//app.UseHttpsRedirection();
app.UsePathBase("/messenger");
// Correlation id middleware should run early so subsequent middleware and controllers log the id
app.UseCorrelationId();
app.UseAuthorization();
app.MapControllers();
app.Run();

// Exposed so WebApplicationFactory<Program> can host the app in integration tests.
// Top-level-statement programs are otherwise internal and can't be used as the generic arg.
public partial class Program { }
