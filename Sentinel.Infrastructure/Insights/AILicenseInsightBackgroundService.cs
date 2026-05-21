using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sentinel.Application.Common.Interfaces;
using Sentinel.Application.DTOs.Insights;
using Sentinel.Domain.Entities;
using Sentinel.Infrastructure.Persistence.Context;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.Infrastructure.Insights
{
    public class AILicenseInsightBackgroundService : BackgroundService
    {
        private readonly ILogger<AILicenseInsightBackgroundService> _logger;
        private readonly IServiceProvider _serviceProvider;

        public AILicenseInsightBackgroundService(ILogger<AILicenseInsightBackgroundService> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("AILicenseInsightBackgroundService is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessLicenseInsightsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred executing AILicenseInsightBackgroundService.");
                }

                // Wait 1 hour before next run
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }

        private async Task ProcessLicenseInsightsAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<SentinelDbContext>();
            var aiService = scope.ServiceProvider.GetRequiredService<IOpenAILicenseInsightService>();

            // Find package licenses with Medium, High or Unknown risk
            var riskyLicenses = await dbContext.PackageLicenses
                .Include(pl => pl.License)
                .Where(pl => pl.License.RiskLevel == "Medium" || pl.License.RiskLevel == "High" || pl.License.RiskLevel == "Unknown")
                .ToListAsync(cancellationToken);

            if (!riskyLicenses.Any())
                return;

            // Get PURLs of already analyzed packages
            var existingInsightsPurls = await dbContext.PackageLicenseInsights
                .Select(i => i.Purl)
                .ToListAsync(cancellationToken);

            var pendingAnalysis = riskyLicenses
                .Where(pl => !existingInsightsPurls.Contains(pl.Purl))
                .ToList();

            if (!pendingAnalysis.Any())
                return;

            _logger.LogInformation("Found {Count} risky package licenses pending AI analysis.", pendingAnalysis.Count);

            // Group by Purl to avoid duplicate requests for the same package version
            var distinctPackages = pendingAnalysis
                .GroupBy(p => p.Purl)
                .Select(g => g.First())
                .ToList();

            // Prepare requests
            var requests = distinctPackages.Select(p => new LicenseInsightRequest
            {
                Purl = p.Purl,
                PackageName = ExtractPackageNameFromPurl(p.Purl),
                CurrentLicense = p.License.Name
            }).ToList();

            // Call AI Service
            var aiResponse = await aiService.GenerateInsightsAsync(requests, cancellationToken);

            if (aiResponse?.Insights != null && aiResponse.Insights.Any())
            {
                foreach (var insight in aiResponse.Insights)
                {
                    var newInsight = new PackageLicenseInsight
                    {
                        Purl = insight.Purl,
                        RiskExplanationForManagement = insight.RiskExplanationForManagement,
                        RecommendedAlternativesJson = JsonSerializer.Serialize(insight.RecommendedAlternatives)
                    };

                    dbContext.PackageLicenseInsights.Add(newInsight);
                }

                await dbContext.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Successfully saved {Count} new AI insights.", aiResponse.Insights.Count);
            }
        }

        private string ExtractPackageNameFromPurl(string purl)
        {
            // Simple PURL parser for name, e.g., pkg:nuget/AutoMapper@13.0.1
            try
            {
                var parts = purl.Split('/');
                if (parts.Length > 1)
                {
                    var nameAndVersion = parts[1].Split('@');
                    return nameAndVersion[0];
                }
                return purl;
            }
            catch
            {
                return purl;
            }
        }
    }
}
