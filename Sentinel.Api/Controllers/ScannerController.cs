using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sentinel.Application.Abstractions;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Sentinel.Api.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class ScannerController : ControllerBase
    {
        private readonly IScannerService _scannerService;

        public ScannerController(IScannerService scannerService)
        {
            _scannerService = scannerService;
        }

        [HttpPost("upload/{moduleId}")]
        public async Task<IActionResult> UploadSbom(Guid moduleId, IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("Geçersiz dosya.");
            }

            using var reader = new StreamReader(file.OpenReadStream());
            var fileContent = await reader.ReadToEndAsync();

            var result = await _scannerService.RunScanAsync(moduleId, file.FileName, fileContent);
            if (!result.Success)
            {
                return BadRequest(result);
            }

            return Ok(result);
        }

        [HttpGet("status/{moduleId}")]
        public async Task<IActionResult> GetScanStatus(Guid moduleId)
        {
            var result = await _scannerService.GetScanStatusAsync(moduleId);
            return Ok(result);
        }
    }
}
