using Sentinel.Application.DTOs.Insights;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.Application.Common.Interfaces
{
    public interface IOpenAILicenseInsightService
    {
        Task<LicenseInsightResponse> GenerateInsightsAsync(List<LicenseInsightRequest> requests, CancellationToken cancellationToken = default);
    }
}
