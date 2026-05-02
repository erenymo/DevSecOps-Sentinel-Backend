using System;
using System.ComponentModel.DataAnnotations;

namespace Sentinel.Application.DTOs.Requests
{
    public class UpdateVexStatusRequest
    {
        [Required]
        public Guid ComponentId { get; set; }
        
        [Required]
        public string ExternalId { get; set; } = string.Empty;

        [Required]
        public string Status { get; set; } = string.Empty;
    }
}
