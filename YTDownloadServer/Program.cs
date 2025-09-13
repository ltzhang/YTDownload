using YTDownloadServer.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<DownloadService>();
builder.Services.AddSingleton<DownloadQueueService>(provider =>
{
    var downloadService = provider.GetRequiredService<DownloadService>();
    var logger = provider.GetRequiredService<ILogger<DownloadQueueService>>();
    return new DownloadQueueService(downloadService, logger, maxConcurrentDownloads: 2);
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowExtension",
        policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        });
});

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("AllowExtension");

app.MapControllers();

app.Run("http://0.0.0.0:5000");