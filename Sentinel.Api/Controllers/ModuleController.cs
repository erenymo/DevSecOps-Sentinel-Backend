using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sentinel.Application.Abstractions;
using Sentinel.Application.DTOs.Requests;
using System.Security.Claims;

namespace Sentinel.Api.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class ModuleController : ControllerBase
    {
        private readonly IModuleService _moduleService;
        private readonly ISbomExportService _sbomExportService;

        public ModuleController(IModuleService moduleService, ISbomExportService sbomExportService)
        {
            _moduleService = moduleService;
            _sbomExportService = sbomExportService;
        }

        private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        [HttpGet("{id}/export-sbom")]
        public async Task<IActionResult> ExportSbom(Guid id)
        {
            var result = await _sbomExportService.ExportSbomJsonAsync(id, UserId);
            if (!result.Success) return BadRequest(result);

            var moduleResult = await _moduleService.GetByIdAsync(id, UserId);
            var rawModuleName = (moduleResult.Success && moduleResult.Data != null) ? (moduleResult.Data.Name ?? "module") : "module";
            // Sanitize module name for filename
            var moduleName = string.Join("_", rawModuleName.Split(Path.GetInvalidFileNameChars()));
            
            var dateStr = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var fileName = $"{moduleName}-sbom-{dateStr}.json";

            var bytes = System.Text.Encoding.UTF8.GetBytes(result.Data ?? "{}");
            return File(bytes, "application/json", fileName);
        }

        [HttpGet("getByWorkspace/{workspaceId}")]
        public async Task<IActionResult> GetByWorkspace(Guid workspaceId)
        {
            var result = await _moduleService.GetByWorkspaceAsync(workspaceId, UserId);
            if (!result.Success) return NotFound(result);
            return Ok(result);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var result = await _moduleService.GetByIdAsync(id, UserId);
            if (!result.Success) return NotFound(result);
            return Ok(result);
        }

        [HttpPost("create")]
        public async Task<IActionResult> Create([FromBody] CreateModuleRequest request)
        {
            var result = await _moduleService.CreateAsync(request.WorkspaceId, request.Module, UserId);
            return Ok(result);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var result = await _moduleService.DeleteAsync(id, UserId);
            if (!result.Success) return NotFound(result);
            return Ok(result);
        }
    }

    // Wrapper for the POST body
    public record CreateModuleRequest(Guid WorkspaceId, ModuleRequest Module);
}
