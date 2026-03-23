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

                // 2. Bileşenleri ilişkileriyle (ComponentLicenses -> License) birlikte çek
                // Not: UnitOfWork içinde Include desteği olduğunu varsayıyorum. 
                // Yoksa repository'ni bu ilişkileri içerecek (Eager Loading) şekilde güncellemelisin.
                var components = await _unitOfWork.Components.GetAllAsync(
                    c => c.ScanId == latestScan.Id,
                    includeProperties: "ComponentLicenses.License" // İlişkili veriyi dahil ediyoruz
                );

                // 3. Yeni ComponentDto yapısına göre (Many-to-Many uyumlu) haritalama yap
                var dtos = components.Select(c => new ComponentDto(
                    c.Id,
                    c.Name,
                    c.Version,
                    c.Purl,
                    c.IsTransitive,
                    c.ParentName,
                    c.DependencyPath,
                    // Birden fazla lisans ismini liste olarak alıyoruz
                    c.ComponentLicenses != null && c.ComponentLicenses.Any()
                        ? c.ComponentLicenses.Select(cl => cl.License.Name).ToList()
                        : new List<string> { "Unknown" }
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
