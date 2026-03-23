using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sentinel.Domain.Entities
{
    public class Component : BaseEntity
    {
        public Guid ScanId { get; set; }
        public virtual Scan Scan { get; set; } = null!;

        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string? Purl { get; set; }
        public bool IsTransitive { get; set; }
        public string? ParentName { get; set; }
        public string? DependencyPath { get; set; }

        public virtual ICollection<VexStatement> VexStatements { get; set; } = new List<VexStatement>();
        public virtual ICollection<ComponentLicense> ComponentLicenses { get; set; } = new List<ComponentLicense>();
    }
}
