using NSnipes.GrpcServer;

var builder = WebApplication.CreateBuilder(args);

// Get port early to check availability
var port = Environment.GetEnvironmentVariable("PORT") 
    ?? builder.Configuration["Port"] 
    ?? "5000";

// Configure Kestrel for HTTP/2 support (required for gRPC)
// HTTP/2 over unencrypted connections (h2c) requires HTTP/2 only protocol
// Note: Health check endpoint won't work with HTTP/2 only, but gRPC has built-in health checks
builder.WebHost.ConfigureKestrel(options =>
{
    var portNum = int.Parse(port);
    
    // Listen on HTTP/2 only (required for gRPC over plain HTTP without TLS)
    // The client must use "Prior Knowledge" mode (Http2UnencryptedSupport switch)
    options.Listen(System.Net.IPAddress.Any, portNum, listenOptions =>
    {
        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
    });
});

// Add services
builder.Services.AddSingleton<GameRoomManager>();
builder.Services.AddSingleton<GameServiceImplementation>(); // Make GameServiceImplementation a singleton so _pendingJoins persists
builder.Services.AddGrpc();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline
app.UseCors("AllowAll");
app.UseGrpcWeb(); // Enable gRPC-Web for browser compatibility (optional)

// Map gRPC service - it will use the singleton instance we registered
app.MapGrpcService<GameServiceImplementation>();

// Note: Health check endpoint removed because HTTP/2 only doesn't support HTTP/1.1 GET requests
// gRPC has built-in health checking via the gRPC health service if needed

app.Logger.LogInformation("NSnipes gRPC Server starting on http://0.0.0.0:{Port}", port);
app.Logger.LogInformation("To use a different port, set the PORT environment variable or update appsettings.json");

try
{
    // Don't pass URL to app.Run() - Kestrel is already configured via ConfigureKestrel
    app.Run();
}
catch (System.IO.IOException ex) when (ex.Message.Contains("address already in use") || ex.Message.Contains("Address already in use") || 
                                        ex.InnerException is System.Net.Sockets.SocketException)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine("❌ Failed to start server: Port {0} is already in use", port);
    Console.Error.WriteLine();
    Console.Error.WriteLine("💡 Solutions:");
    Console.Error.WriteLine("   1. Kill the process using port {0}: lsof -ti :{0} | xargs kill -9", port);
    Console.Error.WriteLine("   2. Use a different port: PORT=5001 ./run-server.sh");
    Console.Error.WriteLine("   3. Update appsettings.json to use a different port");
    Console.Error.WriteLine();
    Environment.Exit(1);
}
catch (Exception)
{
    // Re-throw other exceptions to maintain normal error handling
    throw;
}
