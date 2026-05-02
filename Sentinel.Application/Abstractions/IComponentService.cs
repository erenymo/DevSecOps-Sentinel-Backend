using Sentinel.Application.DTOs;
using Sentinel.Application.DTOs.Responses;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sentinel.Application.Abstractions
{
    public interface IComponentService
    {
        Task<BaseResponse<IEnumerable<ComponentDto>>> GetByModuleIdAsync(Guid moduleId);
        Task<BaseResponse<bool>> UpdateVexStatusAsync(Guid componentId, string externalId, string status);
    }
}
