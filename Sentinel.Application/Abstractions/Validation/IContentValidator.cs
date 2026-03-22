using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sentinel.Application.Abstractions.Validation
{
    public interface IContentValidator
    {
        string Ecosystem { get; }
        HashSet<string> SupportedExtension { get; }
        Task<ValidationResult> ValidateAsync(Stream stream, CancellationToken ct);
    }
}
