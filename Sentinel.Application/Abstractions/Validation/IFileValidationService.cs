using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sentinel.Application.Abstractions.Validation
{
    public interface IFileValidationService
    {
        Task<ValidationResult> ValidateFileAsync(IFormFile file, CancellationToken ct = default);
    }
}
