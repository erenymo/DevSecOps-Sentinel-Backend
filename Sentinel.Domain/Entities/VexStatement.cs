using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sentinel.Domain.Entities
{
    public class VexStatement : BaseEntity
    {
        public Guid ComponentId { get; set; }
        public virtual Component Component { get; set; } = null!;

        public Guid VulnerabilityId { get; set; }
        public virtual Vulnerability Vulnerability { get; set; } = null!;

        public string Status { get; set; } = "Under Investigation"; // VEX Status: affected, not_affected, etc.
        public string? Analysis { get; set; } // AI-generated reachability insights [cite: 29]

        public string? CurrentVersion { get; set; }
        public string? FixedVersion { get; set; }
    }
}
