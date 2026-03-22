using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sentinel.Application.Abstractions;
using Sentinel.Application.Abstractions.Validation;
using Sentinel.Domain.Entities;
using Sentinel.Infrastructure.Persistence.Context;
using Sentinel.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Sentinel.Infrastructure.Persistence.Repositories;
using Sentinel.Infrastructure.Persistence.UnitOfWork;
using System;
using System.Collections.Generic;
using System.Linq;
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

                // Services
                .AddClasses(c => c.Where(t => t.Name.EndsWith("Service")))
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

            return services;
        }
    }
}
