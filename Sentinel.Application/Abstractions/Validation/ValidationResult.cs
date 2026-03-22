using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sentinel.Application.Abstractions.Validation
{
    public record ValidationResult (bool IsSuccess, string? ErrorMessage = null)
    {
        public static ValidationResult Success() => new(true);
        public static ValidationResult Failure(string message) => new(false, message);
    }
}
