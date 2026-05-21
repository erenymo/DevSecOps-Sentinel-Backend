using System.Collections.Generic;

namespace Sentinel.Application.DTOs.Insights
{
    public class LicenseInsightResponse
    {
        public List<LicenseIssueInsight> Insights { get; set; } = new();
    }

    public class LicenseIssueInsight
    {
        public string PackageName { get; set; } = string.Empty;
        public string Purl { get; set; } = string.Empty;
        public string RiskExplanationForManagement { get; set; } = string.Empty;
        public List<AlternativePackage> RecommendedAlternatives { get; set; } = new();
    }

    public class AlternativePackage
    {
        public string PackageName { get; set; } = string.Empty;
        public string LicenseType { get; set; } = string.Empty;
        public string ReasonForRecommendation { get; set; } = string.Empty;
    }
}
