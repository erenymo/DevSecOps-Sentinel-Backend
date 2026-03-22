using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sentinel.Application.Abstractions;
using Sentinel.Application.Abstractions.Validation;
using Sentinel.Infrastructure.Persistence.Context;
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

            return services;
        }
    }
}
