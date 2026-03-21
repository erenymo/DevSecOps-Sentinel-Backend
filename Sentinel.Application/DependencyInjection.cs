using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Scrutor;

namespace Sentinel.Application
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services)
        {
            // 1. AutoMapper Kaydı
            services.AddAutoMapper(Assembly.GetExecutingAssembly());

            // 2. Scrutor ile Akıllı Tarama (Smart Registration)
            services.Scan(selector => selector
                .FromAssemblies(typeof(DependencyInjection).Assembly) // Mevcut assembly'i (Application) tara
                .AddClasses(classes => classes.Where(type =>
                    type.Name.EndsWith("Service") ||
                    type.Name.EndsWith("Strategy"))) // Hem Servisleri hem Stratejileri bul
                .AsImplementedInterfaces() // IScannerService, IParserStrategy gibi arayüzlerle eşleştir
                .WithScopedLifetime()); // İstek bazlı (Scoped) ömür biç

            return services;
        }
    }
}
