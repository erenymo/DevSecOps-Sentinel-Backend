using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sentinel.Application.Abstractions;
using System;
using System.Threading.Tasks;

namespace Sentinel.Api.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class ComponentsController : ControllerBase
    {
        private readonly IComponentService _componentService;

        public ComponentsController(IComponentService componentService)
        {
            _componentService = componentService;
        }

        [HttpGet("module/{moduleId}")]
        public async Task<IActionResult> GetByModuleId(Guid moduleId)
        {
            var result = await _componentService.GetByModuleIdAsync(moduleId);
            if (!result.Success)
            {
                return BadRequest(result);
            }
            return Ok(result);
        }
        [HttpPut("vex-status")]
        public async Task<IActionResult> UpdateVexStatus([FromBody] Sentinel.Application.DTOs.Requests.UpdateVexStatusRequest request)
        {
            var result = await _componentService.UpdateVexStatusAsync(request.ComponentId, request.ExternalId, request.Status);
            if (!result.Success)
            {
                return BadRequest(result);
            }
            return Ok(result);
        }
    }
}
