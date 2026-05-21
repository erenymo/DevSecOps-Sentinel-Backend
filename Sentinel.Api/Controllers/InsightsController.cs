using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sentinel.Application.Common.Interfaces;
using Sentinel.Application.DTOs.Insights;
using Sentinel.Domain.Entities;
using Sentinel.Infrastructure.Persistence.Context;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Sentinel.Api.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class InsightsController : ControllerBase
    {
        private readonly SentinelDbContext _dbContext;
        private readonly IOpenAILicenseInsightService _aiService;

        public InsightsController(SentinelDbContext dbContext, IOpenAILicenseInsightService aiService)
        {
            _dbContext = dbContext;
            _aiService = aiService;
        }

        [HttpPost("license/{moduleId}/analyze")]
        public async Task<IActionResult> AnalyzeLicenseInsights(Guid moduleId)
        {
            // Modüle ait component'lerin PURL ve Lisans bilgilerini al
            var components = await _dbContext.Components
                .Where(c => c.Scan.ModuleId == moduleId)
                .Select(c => new { c.Purl })
                .ToListAsync();

            if (!components.Any())
                return Ok(new { Success = true, Message = "No components found." });

            var purls = components.Select(c => c.Purl).Distinct().ToList();

            // PURL'lere ait riskli PackageLicenses'ları bul
            var riskyLicenses = await _dbContext.PackageLicenses
                .Include(pl => pl.License)
                .Where(pl => purls.Contains(pl.Purl) && (pl.License.RiskLevel == "Medium" || pl.License.RiskLevel == "High" || pl.License.RiskLevel == "Unknown"))
                .ToListAsync();

            if (!riskyLicenses.Any())
                return Ok(new { Success = true, Message = "No risky or unknown licenses found." });

            // Zaten analiz edilmiş olanların PURL'lerini al
            var existingInsightsPurls = await _dbContext.PackageLicenseInsights
                .Where(i => purls.Contains(i.Purl))
                .Select(i => i.Purl)
                .ToListAsync();

            var pendingAnalysis = riskyLicenses
                .Where(pl => !existingInsightsPurls.Contains(pl.Purl))
                .GroupBy(p => p.Purl)
                .Select(g => g.First())
                .ToList();

            if (!pendingAnalysis.Any())
                return Ok(new { Success = true, Message = "All risky licenses are already analyzed." });

            // İstekleri hazırla
            var requests = pendingAnalysis.Select(p => new LicenseInsightRequest
            {
                Purl = p.Purl,
                PackageName = ExtractPackageNameFromPurl(p.Purl),
                CurrentLicense = p.License.Name
            }).ToList();

            // AI'ı çağır
            var aiResponse = await _aiService.GenerateInsightsAsync(requests);

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

                    _dbContext.PackageLicenseInsights.Add(newInsight);
                }

                await _dbContext.SaveChangesAsync();
                return Ok(new { Success = true, Message = $"Successfully saved {aiResponse.Insights.Count} new AI insights." });
            }

            return Ok(new { Success = false, Message = "AI analysis failed or returned no insights." });
        }

        private string ExtractPackageNameFromPurl(string purl)
        {
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
