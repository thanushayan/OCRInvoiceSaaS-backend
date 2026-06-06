using AutoMapper;
using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Libs;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Services;

public class AuthService : IAuthService
{
    private readonly ApplicationDbContext _db;
    private readonly JwtHelper _jwtHelper;
    private readonly IMapper _mapper;
    private readonly ILoginThrottleService _throttle;
    private readonly RefreshTokenService _refreshTokenService;
    private readonly ITwoFactorService _twoFactorService;

    public AuthService(
        ApplicationDbContext db,
        JwtHelper jwtHelper,
        IMapper mapper,
        ILoginThrottleService throttle,
        RefreshTokenService refreshTokenService,
        ITwoFactorService twoFactorService)
    {
        _db = db;
        _jwtHelper = jwtHelper;
        _mapper = mapper;
        _throttle = throttle;
        _refreshTokenService = refreshTokenService;
        _twoFactorService = twoFactorService;
    }

    public async Task<ServiceResult<AuthResponse>> RegisterAsync(RegisterRequest request)
    {
        var email = request.Email.ToLower().Trim();

        if (await _db.Users.AnyAsync(u => u.Email == email))
            return ServiceResult<AuthResponse>.Fail("Email is already in use.", 409);

        var user = new User
        {
            FullName = request.FullName.Trim(),
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password)
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var (token, expiresAt) = _jwtHelper.GenerateToken(user.Id, user.Email, user.FullName);

        return ServiceResult<AuthResponse>.Success(new AuthResponse
        {
            Token = token,
            FullName = user.FullName,
            Email = user.Email,
            UserId = user.Id,
            ExpiresAt = expiresAt
        }, 201);
    }

    public async Task<ServiceResult<AuthResponse>> LoginAsync(LoginRequest request)
        => await LoginAsync(request, null, null);

    public async Task<ServiceResult<AuthResponse>> LoginAsync(
        LoginRequest request, string? ipAddress, string? deviceInfo)
    {
        var email = request.Email.ToLower().Trim();

        var throttleCheck = await _throttle.CheckAsync(email);
        if (!throttleCheck.IsSuccess)
            return ServiceResult<AuthResponse>.Fail(throttleCheck.Error!, throttleCheck.StatusCode);

        var user = await _db.Users
            .Include(u => u.TwoFactor)
            .FirstOrDefaultAsync(u => u.Email == email && u.IsActive);

        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            await _throttle.RecordFailureAsync(email, ipAddress, "InvalidCredentials");
            return ServiceResult<AuthResponse>.Fail("Invalid email or password.", 401);
        }

        await _throttle.RecordSuccessAsync(email, ipAddress);

        if (user.TwoFactorEnabled && user.TwoFactor?.IsEnabled == true)
        {
            if (user.TwoFactor.Method == TwoFactorMethod.EmailOtp)
                await _twoFactorService.SendEmailOtpAsync(user.Id);

            return ServiceResult<AuthResponse>.Success(new AuthResponse
            {
                RequiresTwoFactor = true,
                TwoFactorMethod = user.TwoFactor.Method.ToString(),
                UserId = user.Id,
                Token = string.Empty,
                FullName = user.FullName,
                Email = SecurityHelper.MaskEmail(user.Email),
                ExpiresAt = DateTime.UtcNow
            });
        }

        var (access, accessExpiry, refresh, refreshExpiry) =
            await _refreshTokenService.IssueTokensAsync(user, ipAddress, deviceInfo);

        return ServiceResult<AuthResponse>.Success(new AuthResponse
        {
            Token = access,
            RefreshToken = refresh,
            FullName = user.FullName,
            Email = user.Email,
            UserId = user.Id,
            ExpiresAt = accessExpiry,
            RefreshTokenExpiresAt = refreshExpiry
        });
    }

    public async Task<ServiceResult<UserProfileResponse>> GetCurrentUserAsync(Guid userId)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null)
            return ServiceResult<UserProfileResponse>.Fail("User not found.", 404);

        return ServiceResult<UserProfileResponse>.Success(_mapper.Map<UserProfileResponse>(user));
    }
}
