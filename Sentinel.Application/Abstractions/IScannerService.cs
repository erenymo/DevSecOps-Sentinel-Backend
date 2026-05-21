using Sentinel.Application.DTOs.Responses;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sentinel.Application.Abstractions
{
    public interface IScannerService
    {
        Task<BaseResponse<Guid>> RunScanAsync(Guid moduleId, string fileName, string fileContent);
        Task<BaseResponse<ScanStatusDto>> GetScanStatusAsync(Guid moduleId);
    }
}
