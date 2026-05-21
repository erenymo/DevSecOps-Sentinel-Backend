using Sentinel.Application.Abstractions;
using Sentinel.Application.DTOs.Responses;
using Sentinel.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sentinel.Application.Services
{
    public class ScannerService : IScannerService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IEnumerable<IParserStrategy> _parsers;
        private readonly ILicenseEnrichmentQueue _licenseQueue;
        private readonly IVulnerabilityEnrichmentQueue _vulnQueue;

        public ScannerService(
            IUnitOfWork unitOfWork,
            IEnumerable<IParserStrategy> parsers,
            ILicenseEnrichmentQueue licenseQueue,
            IVulnerabilityEnrichmentQueue vulnQueue)
        {
            _unitOfWork = unitOfWork;
            _parsers = parsers;
            _licenseQueue = licenseQueue;
            _vulnQueue = vulnQueue;
        }

        public async Task<BaseResponse<Guid>> RunScanAsync(Guid moduleId, string fileName, string fileContent)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fileContent))
                    return BaseResponse<Guid>.Fail("Geçersiz dosya formatı. Lütfen project.assets.json yükleyin.", "Unsupported File Format");

                var module = await _unitOfWork.Modules.GetByIdAsync(moduleId);
                if (module == null) return BaseResponse<Guid>.Fail("Modül bulunamadı.", "Module Not Found");
                var parser = _parsers.FirstOrDefault(p => p.Ecosystem == module.Ecosystem);
                if (parser == null) return BaseResponse<Guid>.Fail($"{module.Ecosystem} için uygur parser bulunamadı.", "Unsupported Ecosystem");

                var scan = new Scan
                {
                    ModuleId = moduleId,
                    ScanDate = DateTime.UtcNow,
                    SbomOutput = "{}" // will be CycloneDX format.
                };
                await _unitOfWork.Scans.AddAsync(scan);

                var extension = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
                using var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(fileContent));
                List<Component> components = await parser.ParseAsync(stream, extension, scan.Id);

                foreach (var comp in components)
                {
                    await _unitOfWork.Components.AddAsync(comp);
                }

                await _unitOfWork.SaveChangesAsync();

                if (components.Count > 0)
                {
                    // Tarama başarılı ve bileşen bulundu → Kuyruklara ekle
                    await _licenseQueue.EnqueueScanAsync(scan.Id);
                    await _vulnQueue.EnqueueScanAsync(scan.Id);
                }
                else
                {
                    // Bağımlılık bulunamadı → Yükleme bitti say ve kuyrukları atla
                    scan.VulnEnrichmentCompleted = true;
                    scan.LicenseEnrichmentCompleted = true;
                    _unitOfWork.Scans.Update(scan);
                    await _unitOfWork.SaveChangesAsync();
                }

                return BaseResponse<Guid>.Ok(scan.Id, "Tarama başarıyla tamamlandı.");
            }
            catch (Exception ex)
            {
                return BaseResponse<Guid>.Fail($"Tarama sırasında hata oluştu: {ex.Message}");
            }
        }

        public async Task<BaseResponse<ScanStatusDto>> GetScanStatusAsync(Guid moduleId)
        {
            var scans = await _unitOfWork.Scans.GetAllAsync(s => s.ModuleId == moduleId);
            var latestScan = scans.OrderByDescending(s => s.ScanDate).FirstOrDefault();

            if (latestScan == null)
            {
                return BaseResponse<ScanStatusDto>.Ok(new ScanStatusDto
                {
                    IsDependenciesParsed = false,
                    IsVulnEnrichmentCompleted = false,
                    IsLicenseEnrichmentCompleted = false
                }, "No scan found");
            }

            return BaseResponse<ScanStatusDto>.Ok(new ScanStatusDto
            {
                IsDependenciesParsed = true, // Dependencies parsing is synchronous during upload
                IsVulnEnrichmentCompleted = latestScan.VulnEnrichmentCompleted,
                IsLicenseEnrichmentCompleted = latestScan.LicenseEnrichmentCompleted
            }, "Status fetched");
        }
    }
}

