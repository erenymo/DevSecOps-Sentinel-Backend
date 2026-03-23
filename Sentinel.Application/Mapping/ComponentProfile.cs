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
            // LicenseName (tekil) yerine LicenseNames (liste) eşlemesi yapıyoruz
            .ForMember(dest => dest.LicenseNames,
                opt => opt.MapFrom(src =>
                    src.ComponentLicenses != null && src.ComponentLicenses.Any()
                        ? src.ComponentLicenses.Select(cl => cl.License.Name).ToList()
                        : new List<string> { "Unknown" }));
        }
    }
}
