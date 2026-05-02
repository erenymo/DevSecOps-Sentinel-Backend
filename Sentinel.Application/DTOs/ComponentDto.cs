using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sentinel.Application.DTOs
{
    public record ComponentVulnerabilityDto(
        string ExternalId,
        string? VulnerabilityId,
        string? SeverityType,
        decimal? SeverityScore,
        string? SeverityLevel,
        string Status,
        string? CurrentVersion,
        string? FixedVersion
    );

    public record ComponentDto(
        Guid Id, 
        string Name, 
        string Version, 
        string? Purl, 
        bool IsTransitive, 
        string? ParentName, 
        string? DependencyPath, 
        List<string>? LicenseNames,
        List<ComponentVulnerabilityDto>? Vulnerabilities = null
    );
}
