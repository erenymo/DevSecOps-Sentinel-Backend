using Microsoft.EntityFrameworkCore;
using Sentinel.Application;
using Sentinel.Application.Abstractions;
using Sentinel.Application.Parsers;
using Sentinel.Application.Services;
using Sentinel.Infrastructure;
using Sentinel.Infrastructure.Persistence.Context;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
// 1. CORS Politikasýný Tanýmla
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()   // Tüm kaynaklara izin ver (Geliþtirme aþamasý için)
              .AllowAnyMethod()   // GET, POST, PUT vb. hepsine izin ver
              .AllowAnyHeader();  // Tüm Header'lara izin ver
    });
});

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddDbContext<SentinelDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddApplicationServices();

var app = builder.Build();

app.UseCors("AllowAll");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.MapControllers();
app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
