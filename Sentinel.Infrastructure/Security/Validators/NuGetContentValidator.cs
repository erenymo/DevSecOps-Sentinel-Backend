using Sentinel.Application.Abstractions.Validation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml;

namespace Sentinel.Infrastructure.Security.Validators
{
    public class NuGetContentValidator : IContentValidator
    {
        public string Ecosystem => "NuGet";
        public HashSet<string> SupportedExtension => new() { ".csproj", ".json" };

        public async Task<ValidationResult> ValidateAsync(Stream stream, CancellationToken ct)
        {
            // Stream başa alınmalı (çok kritik!)
            if (stream.CanSeek)
                stream.Position = 0;

            // İlk karakteri oku (lightweight magic check)
            var buffer = new byte[1];
            await stream.ReadAsync(buffer, 0, 1, ct);

            if (stream.CanSeek)
                stream.Position = 0;

            char firstChar = (char)buffer[0];

            try
            {
                // XML mi JSON mı karar ver
                if (firstChar == '<')
                {
                    return await ValidateXmlAsync(stream, ct);
                }
                else if (firstChar == '{' || firstChar == '[')
                {
                    return await ValidateJsonAsync(stream, ct);
                }
                else
                {
                    return ValidationResult.Failure("Unknown or unsupported file content.");
                }
            }
            catch
            {
                return ValidationResult.Failure("Invalid file structure or malicious content detected.");
            }
        }

        private async Task<ValidationResult> ValidateXmlAsync(Stream stream, CancellationToken ct)
        {
            var settings = new XmlReaderSettings
            {
                Async = true,
                DtdProcessing = DtdProcessing.Prohibit, // 🔒 XXE protection
                XmlResolver = null // 🔒 External entity disable
            };

            using var reader = XmlReader.Create(stream, settings);

            while (await reader.ReadAsync())
            {
                // intentionally empty → parser zorlanır
            }

            return ValidationResult.Success();
        }

        private async Task<ValidationResult> ValidateJsonAsync(Stream stream, CancellationToken ct)
        {
            using var jsonDoc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            // Opsiyonel: NuGet'e özel kontrol (çok iyi olur 👇)
            if (jsonDoc.RootElement.ValueKind != JsonValueKind.Object)
                return ValidationResult.Failure("Invalid JSON structure.");

            return ValidationResult.Success();
        }
    }
}
