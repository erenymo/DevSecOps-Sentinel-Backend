using Sentinel.Application.DTOs.Requests;
using Sentinel.Application.DTOs.Responses;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sentinel.Application.Abstractions
{
    public interface IAuthService
    {
        Task<BaseResponse<TokenResponse>> LoginAsync(LoginRequest request);
        Task<BaseResponse<Guid>> RegisterAsync(RegisterRequest request);
        Task<BaseResponse<TokenResponse>> RefreshTokenAsync(RefreshTokenRequest request);
    }
}
