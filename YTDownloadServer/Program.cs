using YTDownloadServer.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<DownloadService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowExtension",
        policy =>
        {
            policy.WithOrigins("moz-extension://*", "http://localhost:*")
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

app.Run("http://localhost:5000");