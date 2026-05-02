using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sentinel.Application.Abstractions;
using Sentinel.Application.Abstractions.Validation;
using Sentinel.Domain.Entities;
using Sentinel.Infrastructure.Persistence.Context;
using Sentinel.Infrastructure.Security;
using Sentinel.Infrastructure.DepsDev;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Sentinel.Infrastructure.Persistence.Repositories;
using Sentinel.Infrastructure.Persistence.UnitOfWork;
using Polly;
using Polly.Extensions.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Sentinel.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            services.AddDbContext<SentinelDbContext>(options =>
                options.UseNpgsql(connectionString));

            services.AddScoped<IUnitOfWork, UnitOfWork>();

            services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));

            services.Scan(selector => selector
                .FromAssemblies(typeof(DependencyInjection).Assembly)

                // Services (BackgroundService alt sınıflarını hariç tut — onlar AddHostedService ile Singleton kaydedilir)
                .AddClasses(c => c.Where(t => t.Name.EndsWith("Service") && !t.IsSubclassOf(typeof(Microsoft.Extensions.Hosting.BackgroundService))))
                .AsImplementedInterfaces()
                .WithScopedLifetime()

                // Validators
                .AddClasses(c => c.AssignableTo<IContentValidator>())
                .AsImplementedInterfaces()
                .WithScopedLifetime()
            );

            // JWT Settings
            services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
            services.AddScoped<IJwtProvider, JwtProvider>();

            // Identity
            services.AddIdentity<AppUser, AppRole>(options =>
            {
                options.Password.RequireDigit = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireLowercase = false;
            })
            .AddEntityFrameworkStores<SentinelDbContext>()
            .AddDefaultTokenProviders();

            // Authentication & JWT Bearer
            var jwtSettingsSection = configuration.GetSection(JwtSettings.SectionName);
            var secret = jwtSettingsSection.GetValue<string>("Secret") ?? "SuperSecretKeyForDevelopmentAndTestingOnly!!!";
            var issuer = jwtSettingsSection.GetValue<string>("Issuer") ?? "SentinelApi";
            var audience = jwtSettingsSection.GetValue<string>("Audience") ?? "SentinelUsers";

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = issuer,
                    ValidAudience = audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret))
                };
            });

            // ─── deps.dev Entegrasyonu ───────────────────────────────────────

            // In-Memory Cache (lisans sonuçları için)
            services.AddMemoryCache();

            // License Enrichment Queue (Singleton — tüm scope'lar aynı kuyruğu paylaşır)
            services.AddSingleton<ILicenseEnrichmentQueue, LicenseEnrichmentQueue>();

            // DepsDevClient — HttpClientFactory ile Polly (Retry + Circuit Breaker)
            services.AddHttpClient<IDepsDevClient, DepsDevClient>(client =>
            {
                client.BaseAddress = new Uri("https://api.deps.dev/");
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddPolicyHandler(GetRetryPolicy())
            .AddPolicyHandler(GetCircuitBreakerPolicy());

            // Arka plan lisans zenginleştirme servisi
            services.AddHostedService<LicenseEnrichmentBackgroundService>();

            // ─── OSV.dev Entegrasyonu ───────────────────────────────────────

            services.AddSingleton<IVulnerabilityEnrichmentQueue, Sentinel.Infrastructure.Osv.VulnerabilityEnrichmentQueue>();

            services.AddHttpClient<IOsvClient, Sentinel.Infrastructure.Osv.OsvClient>(client =>
            {
                client.BaseAddress = new Uri("https://api.osv.dev/");
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddPolicyHandler(GetRetryPolicy())
            .AddPolicyHandler(GetCircuitBreakerPolicy());

            // Arka plan zafiyet zenginleştirme servisi
            services.AddHostedService<Sentinel.Infrastructure.Osv.VulnerabilityEnrichmentBackgroundService>();

            return services;
        }

        /// <summary>
        /// Retry Policy: HTTP 429 (Too Many Requests) ve 5xx hataları için
        /// exponential backoff ile 3 kez tekrar dene (2s, 4s, 8s).
        /// </summary>
        private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
        {
            return HttpPolicyExtensions
                .HandleTransientHttpError()
                .OrResult(msg => msg.StatusCode == HttpStatusCode.TooManyRequests)
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
        }

        /// <summary>
        /// Circuit Breaker: Art arda 5 hata olursa 30 saniye devre dışı bırak.
        /// </summary>
        private static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
        {
            return HttpPolicyExtensions
                .HandleTransientHttpError()
                .CircuitBreakerAsync(
                    handledEventsAllowedBeforeBreaking: 5,
                    durationOfBreak: TimeSpan.FromSeconds(30));
        }
    }
}

