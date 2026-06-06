using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Interfaces;

public interface IRefreshTokenService
{
    Task<ServiceResult<TokenResponse>> RefreshAsync(RefreshRequest request, string? ipAddress, string? deviceInfo);
    Task<ServiceResult> RevokeAsync(string rawToken, Guid userId);
    Task<ServiceResult> RevokeAllAsync(Guid userId);
    Task<ServiceResult<List<ActiveSessionResponse>>> GetActiveSessionsAsync(Guid userId, string? currentRawToken);
    Task PurgeExpiredAsync();
}

public interface ITwoFactorService
{
    Task<ServiceResult<TwoFactorStatusResponse>> GetStatusAsync(Guid userId);
    Task<ServiceResult<TwoFactorChallengeResponse>> SendEmailOtpAsync(Guid userId);
    Task<ServiceResult<TotpSetupResponse>> SetupTotpAsync(Guid userId);
    Task<ServiceResult> ConfirmTotpSetupAsync(Guid userId, ConfirmTotpRequest request);
    Task<ServiceResult<TokenResponse>> VerifyAndIssueTokensAsync(VerifyTwoFactorRequest request, string? ipAddress, string? deviceInfo);
    Task<ServiceResult> DisableTwoFactorAsync(Guid userId, string currentPassword);
    Task<ServiceResult<TwoFactorChallengeResponse>> ResendOtpAsync(ResendOtpRequest request);
}

public interface IPasswordResetService
{
    Task<ServiceResult> SendResetEmailAsync(ForgotPasswordRequest request, string? ipAddress);
    Task<ServiceResult> ResetPasswordAsync(ResetPasswordRequest request);
    Task<ServiceResult> ChangePasswordAsync(Guid userId, ChangePasswordRequest request);
}

public interface ILoginThrottleService
{
    Task<ServiceResult<AccountStatusResponse>> CheckAsync(string email);
    Task RecordSuccessAsync(string email, string? ipAddress);
    Task RecordFailureAsync(string email, string? ipAddress, string reason);
    Task UnlockAccountAsync(Guid userId);
}

public interface IApiKeyService
{
    Task<ServiceResult<CreateApiKeyResponse>> CreateAsync(Guid userId, Guid companyId, CreateApiKeyRequest request);
    Task<ServiceResult<List<ApiKeyResponse>>> GetByCompanyAsync(Guid companyId, Guid userId);
    Task<ServiceResult> RevokeAsync(Guid keyId, Guid userId);
    Task<(bool valid, Guid userId, Guid companyId, string[] scopes)> ValidateAsync(string rawKey);
}
