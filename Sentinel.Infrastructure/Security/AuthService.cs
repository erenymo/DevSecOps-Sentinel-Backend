using Microsoft.AspNetCore.Identity;
using Sentinel.Application.Abstractions;
using Sentinel.Application.DTOs.Requests;
using Sentinel.Application.DTOs.Responses;
using Sentinel.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace Sentinel.Infrastructure.Security
{
    public class AuthService : IAuthService
    {
        private readonly UserManager<AppUser> _userManager;
        private readonly SignInManager<AppUser> _signInManager;
        private readonly RoleManager<AppRole> _roleManager;
        private readonly IJwtProvider _jwtProvider;

        public AuthService(
            UserManager<AppUser> userManager,
            SignInManager<AppUser> signInManager,
            RoleManager<AppRole>    roleManager,
            IJwtProvider jwtProvider)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _roleManager = roleManager;
            _jwtProvider = jwtProvider;
        }

        public async Task<BaseResponse<TokenResponse>> LoginAsync(LoginRequest request)
        {
            var user = await _userManager.FindByEmailAsync(request.Email);
            if (user == null)
                return BaseResponse<TokenResponse>.Fail("Invalid email or password.");

            var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, false);
            if (!result.Succeeded)
                return BaseResponse<TokenResponse>.Fail("Invalid email or password.");

            var roles = await _userManager.GetRolesAsync(user);
            var token = _jwtProvider.GenerateToken(user, roles);
            var refreshToken = _jwtProvider.GenerateRefreshToken();

            user.RefreshToken = refreshToken;
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7); // from settings optionally
            await _userManager.UpdateAsync(user);

            return BaseResponse<TokenResponse>.Ok(new TokenResponse(token, refreshToken, user.RefreshTokenExpiryTime.Value), "Login successful.");
        }

        public async Task<BaseResponse<Guid>> RegisterAsync(RegisterRequest request)
        {
            var exists = await _userManager.FindByEmailAsync(request.Email);
            if (exists != null)
                return BaseResponse<Guid>.Fail("Email is already in use.");

            var user = new AppUser
            {
                Email = request.Email,
                UserName = request.Email,
                FullName = request.FullName,
                CreatedAt = DateTime.UtcNow
            };

            var result = await _userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded)
                return BaseResponse<Guid>.Fail(result.Errors.Select(e => e.Description).ToList(), "Registration failed.");

            // Create default role if it doesn't exist
            if (!await _roleManager.RoleExistsAsync("User"))
            {
                await _roleManager.CreateAsync(new AppRole { Name = "User", Description = "Default User Role" });
            }

            await _userManager.AddToRoleAsync(user, "User");

            return BaseResponse<Guid>.Ok(user.Id, "User registered successfully.");
        }

        public async Task<BaseResponse<TokenResponse>> RefreshTokenAsync(RefreshTokenRequest request)
        {
            // Note: Token validation logic for extracting principal from expired token goes here 
            // We'll trust the parameter strictly for simplicity or implement proper claim extraction
            var user = _userManager.Users.FirstOrDefault(u => u.RefreshToken == request.RefreshToken);

            if (user == null || user.RefreshTokenExpiryTime <= DateTime.UtcNow)
                return BaseResponse<TokenResponse>.Fail("Invalid or expired refresh token.");

            var roles = await _userManager.GetRolesAsync(user);
            var newAccessToken = _jwtProvider.GenerateToken(user, roles);
            var newRefreshToken = _jwtProvider.GenerateRefreshToken();

            user.RefreshToken = newRefreshToken;
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);
            await _userManager.UpdateAsync(user);

            return BaseResponse<TokenResponse>.Ok(new TokenResponse(newAccessToken, newRefreshToken, user.RefreshTokenExpiryTime.Value), "Token refreshed.");
        }
    }
}
