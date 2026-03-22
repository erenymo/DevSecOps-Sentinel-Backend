using Microsoft.AspNetCore.Http;
using Sentinel.Application.Abstractions.Validation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml;

namespace Sentinel.Infrastructure.Security
{
    public class FileValidationService : IFileValidationService
    {
        private const long MaxFileSize = 5 * 1024 * 1024;

        private readonly IEnumerable<IContentValidator> _validators;

        public FileValidationService(IEnumerable<IContentValidator> validators)
        {
            _validators = validators;
        }
        public async Task<ValidationResult> ValidateFileAsync(IFormFile file, CancellationToken ct = default)
        {
            if (file == null || file.Length == 0)
                return ValidationResult.Failure("File is empty.");

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

            var validator = _validators
                .FirstOrDefault(v => v.SupportedExtension.Contains(extension));

            if (validator is null)
                return ValidationResult.Failure("Unsupported file extension.");

            if (file.Length > MaxFileSize)
                return ValidationResult.Failure("File size exceeds 5MB limit.");

            using var stream = file.OpenReadStream();

            return await validator.ValidateAsync(stream, ct);
        }
    }
}
