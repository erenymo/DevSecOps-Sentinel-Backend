using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Sentinel.Api.Middleware;
using Sentinel.Application;
using Sentinel.Application.Abstractions;
using Sentinel.Application.Parsers;
using Sentinel.Application.Services;
using Sentinel.Infrastructure;
using Sentinel.Infrastructure.Persistence.Context;
using Sentinel.Application.DTOs.Responses;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(e => e.Value?.Errors.Count > 0)
                .SelectMany(x => x.Value!.Errors)
                .Select(x => x.ErrorMessage)
                .ToList();

            var response = BaseResponse<object>.Fail(errors, "Validation Failed");

            return new BadRequestObjectResult(response);
        };
    });

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi(options =>
{
    // JWT Yetkilendirmesi eklemek istersen (DevSecOps odaðýna uygun olarak)
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info.Title = "Sentinel API";
        document.Info.Version = "v1";
        return Task.CompletedTask;
    });
});
builder.Services.AddDbContext<SentinelDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddApplicationServices();

var app = builder.Build();

app.UseGlobalExceptionMiddleware();

app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); // /openapi/v1.json dosyasýný üretir

    app.UseSwaggerUI(options =>
    {
        // ÖNEMLÝ: Endpoint yolu MapOpenApi'nin ürettiði JSON ile ayný olmalý
        options.SwaggerEndpoint("/openapi/v1.json", "Sentinel API v1");
        options.RoutePrefix = "swagger";
    });
}

app.UseHttpsRedirection();
app.MapControllers();
app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
