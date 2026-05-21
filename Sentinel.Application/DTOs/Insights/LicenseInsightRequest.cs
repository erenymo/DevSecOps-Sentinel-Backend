using System;

namespace Sentinel.Application.DTOs.Insights
{
    public class LicenseInsightRequest
    {
        public string Purl { get; set; } = string.Empty;
        public string PackageName { get; set; } = string.Empty;
        public string CurrentLicense { get; set; } = string.Empty;
    }
}
