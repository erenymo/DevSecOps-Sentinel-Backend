using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sentinel.Domain.Entities
{
    public class Scan : BaseEntity
    {
        public Guid ModuleId { get; set; }
        public virtual Module Module { get; set; } = null!;

        public DateTime ScanDate { get; set; } = DateTime.UtcNow;
        public double SecurityScore { get; set; }
        public double LicenseRiskScore { get; set; }

        [Column(TypeName = "jsonb")]
        public string SbomOutput { get; set; } = "{}"; // CycloneDX 1.5 JSON [cite: 28]

        public bool VulnEnrichmentCompleted { get; set; } = false;
        public bool LicenseEnrichmentCompleted { get; set; } = false;

        public virtual ICollection<Component> Components { get; set; } = new List<Component>();
    }
}
