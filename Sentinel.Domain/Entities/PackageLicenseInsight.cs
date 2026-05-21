using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sentinel.Domain.Entities
{
    public class PackageLicenseInsight : BaseEntity
    {
        public string Purl { get; set; } = string.Empty;
        public string RiskExplanationForManagement { get; set; } = string.Empty;
        public string RecommendedAlternativesJson { get; set; } = string.Empty; // Store alternatives as JSON array
    }
}
