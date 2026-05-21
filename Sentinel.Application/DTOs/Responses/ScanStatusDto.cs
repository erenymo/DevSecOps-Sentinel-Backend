using System;

namespace Sentinel.Application.DTOs.Responses
{
    public class ScanStatusDto
    {
        public bool IsDependenciesParsed { get; set; }
        public bool IsVulnEnrichmentCompleted { get; set; }
        public bool IsLicenseEnrichmentCompleted { get; set; }
    }
}
