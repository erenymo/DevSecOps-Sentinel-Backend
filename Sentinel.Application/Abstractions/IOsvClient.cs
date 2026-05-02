using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.Application.Abstractions
{
    public record OsvVulnerabilityDto(
        string Id, 
        string? Alias, 
        string Summary, 
        string Details, 
        string? SeverityType, 
        decimal? SeverityScore, 
        string? SeverityLevel, 
        string? FixedVersion, 
        string RawData
    );

    public interface IOsvClient
    {
        Task<IReadOnlyList<OsvVulnerabilityDto>> GetVulnerabilitiesAsync(string purl, string version, CancellationToken cancellationToken = default);
    }
}
