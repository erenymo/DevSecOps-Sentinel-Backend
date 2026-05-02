using Sentinel.Application.Abstractions;
using Sentinel.Application.DTOs;
using Sentinel.Application.DTOs.Responses;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sentinel.Application.Services
{
    public class ComponentService : IComponentService
    {
        private readonly IUnitOfWork _unitOfWork;

        public ComponentService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<BaseResponse<IEnumerable<ComponentDto>>> GetByModuleIdAsync(Guid moduleId)
        {
            try
            {
                // 1. En güncel taramayı bul (Daha önce olduğu gibi)
                var scans = await _unitOfWork.Scans.GetAllAsync(s => s.ModuleId == moduleId);
                var latestScan = scans.OrderByDescending(s => s.ScanDate).FirstOrDefault();

                if (latestScan == null)
                {
                    return BaseResponse<IEnumerable<ComponentDto>>.Ok(new List<ComponentDto>(), "Bu modül için henüz tarama yapılmamış.");
                }

                // 2. Bileşenleri çek
                var components = await _unitOfWork.Components.GetAllAsync(
                    c => c.ScanId == latestScan.Id,
                    includeProperties: "VexStatements.Vulnerability"
                );

                var componentList = components.ToList();

                var purls = componentList
                    .Where(c => !string.IsNullOrWhiteSpace(c.Purl))
                    .Select(c => c.Purl!)
                    .Distinct()
                    .ToList();

                var packageLicenses = await _unitOfWork.PackageLicenses.GetAllAsync(
                    pl => purls.Contains(pl.Purl),
                    includeProperties: "License"
                );

                var licenseMap = packageLicenses
                    .GroupBy(pl => pl.Purl)
                    .ToDictionary(g => g.Key, g => g.Select(pl => pl.License.Name).ToList());

                // 3. Yeni ComponentDto yapısına göre haritalama yap
                var dtos = componentList.Select(c => new ComponentDto(
                    c.Id,
                    c.Name,
                    c.Version,
                    c.Purl,
                    c.IsTransitive,
                    c.ParentName,
                    c.DependencyPath,
                    // PURL üzerinden lisans isimlerini alıyoruz
                    !string.IsNullOrWhiteSpace(c.Purl) && licenseMap.TryGetValue(c.Purl, out var licenses)
                        ? licenses
                        : new List<string>(),
                    c.VexStatements?.Select(v => new ComponentVulnerabilityDto(
                        v.Vulnerability.ExternalId,
                        v.Vulnerability.VulnerabilityId,
                        v.Vulnerability.SeverityType,
                        v.Vulnerability.SeverityScore,
                        v.Vulnerability.SeverityLevel,
                        v.Status,
                        v.CurrentVersion,
                        v.FixedVersion
                    )).ToList()
                ));

                return BaseResponse<IEnumerable<ComponentDto>>.Ok(dtos.ToList(), "Bileşenler başarıyla getirildi.");
            }
            catch (Exception ex)
            {
                return BaseResponse<IEnumerable<ComponentDto>>.Fail($"Hata oluştu: {ex.Message}");
            }
        }
    }
}

