using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sentinel.Domain.Entities
{
    public class ComponentLicense : BaseEntity
    {
        public Guid ComponentId { get; set; }
        public Guid LicenseId { get; set; }
        public virtual Component Component { get; set; } = null!;
        public virtual License License { get; set; } = null!;
    }
}
