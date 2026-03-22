using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sentinel.Application.DTOs
{
    public record ComponentDto(Guid Id, string Name, string Version, string? Purl, bool IsTransitive, string? ParentName, string? DependencyPath, string? LicenseName);
}
