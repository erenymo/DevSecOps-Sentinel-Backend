using System;
using System.Threading.Tasks;
using Sentinel.Application.DTOs.Responses;

namespace Sentinel.Application.Abstractions
{
    public interface ISbomExportService
    {
        Task<BaseResponse<string>> ExportSbomJsonAsync(Guid moduleId, Guid ownerId);
    }
}
