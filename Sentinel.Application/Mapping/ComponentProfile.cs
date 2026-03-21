using Sentinel.Application.DTOs;
using System;
using System.Collections.Generic;
using Sentinel.Domain.Entities;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AutoMapper;

namespace Sentinel.Application.Mapping
{
    public class ComponentProfile : Profile
    {
        public ComponentProfile()
        {
            CreateMap<Component, ComponentDto>()
            // Id, Name, Version, Purl ve IsTransitive alanları 
            // isimleri birebir aynı olduğu için otomatik eşleşir.

            // License tablosundan Name bilgisini güvenli bir şekilde alıyoruz.
            // Eğer License null ise "Unknown" veya "N/A" döner.
            .ForMember(dest => dest.LicenseName,
                opt => opt.MapFrom(src => src.License != null ? src.License.Name : "Unknown"));

            // Entity'den DTO'ya dönüşüm (Okuma işlemleri için)
            CreateMap<Component, ComponentDto>().ReverseMap();
        }
    }
}
