using PDFApi.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddSingleton<IBrowsingService, PuppeteerBrowserService>();
builder.Services.AddSingleton<ISecureStorageService, AesStorageService>();
builder.Services.AddSingleton<IReportQueue, InMemoryReportQueue>();

builder.Services.AddHostedService<PdfProcessorWorker>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();



app.Run();
