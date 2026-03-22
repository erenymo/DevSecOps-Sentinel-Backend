using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sentinel.Application.DTOs.Responses
{
    public record TokenResponse(string Token, string RefreshToken, DateTime Expiration);
}
