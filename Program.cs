using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Libs;
using OcrInvoiceSaaS.Mapping;
using OcrInvoiceSaaS.Middleware;
using OcrInvoiceSaaS.Quartz;
using OcrInvoiceSaaS.Services;
using Quartz;

var builder = WebApplication.CreateBuilder(args);

// ── Database ──────────────────────────────────────────────────────────────
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── AutoMapper ────────────────────────────────────────────────────────────
builder.Services.AddAutoMapper(typeof(AutoMapperProfile));

// ── JWT Authentication ────────────────────────────────────────────────────
var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = jwtSection["Issuer"],
            ValidAudience            = jwtSection["Audience"],
            IssuerSigningKey         = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSection["Secret"]!)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();

// ── Core Services ─────────────────────────────────────────────────────────
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ICompanyService, CompanyService>();
builder.Services.AddScoped<IVendorService, VendorService>();
builder.Services.AddScoped<IInvoiceService, InvoiceServiceV2>();
builder.Services.AddScoped<InvoiceServiceV2>();
builder.Services.AddScoped<IOcrService, OcrService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IExpenseCategoryService, ExpenseCategoryService>();
builder.Services.AddScoped<IInvoiceItemService, InvoiceItemService>();
builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();
builder.Services.AddScoped<ICompanyMemberService, CompanyMemberService>();
builder.Services.AddScoped<IAuditService, AuditService>();

// ── Security Services ─────────────────────────────────────────────────────
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
builder.Services.AddScoped<RefreshTokenService>();
builder.Services.AddScoped<ITwoFactorService, TwoFactorService>();
builder.Services.AddScoped<IPasswordResetService, PasswordResetService>();
builder.Services.AddScoped<ILoginThrottleService, LoginThrottleService>();
builder.Services.AddScoped<IApiKeyService, ApiKeyService>();


// ── Invoice Intelligence Services ─────────────────────────────────────────
builder.Services.AddScoped<IDuplicateDetectionService, DuplicateDetectionService>();
builder.Services.AddScoped<IApprovalWorkflowService, ApprovalWorkflowService>();
builder.Services.AddScoped<IBulkOcrService, BulkOcrService>();
builder.Services.AddScoped<IPurchaseOrderService, PurchaseOrderService>();
builder.Services.AddScoped<IInvoiceMatchingService, InvoiceMatchingService>();
builder.Services.AddScoped<ICurrencyConversionService, CurrencyConversionService>();

// HttpClient for currency API
builder.Services.AddHttpClient("OxrClient");


// ── Platform Services ─────────────────────────────────────────────────────
builder.Services.AddScoped<IReportingService, ReportingService>();
builder.Services.AddScoped<IWebhookService, WebhookService>();
builder.Services.AddScoped<IActivityService, ActivityService>();
builder.Services.AddScoped<IRateLimitService, RateLimitService>();
builder.Services.AddHttpClient("WebhookClient");


// ── Notifications & Communication ─────────────────────────────────────────
builder.Services.AddScoped<INotificationDispatcher, NotificationDispatcher>();
builder.Services.AddScoped<MentionService>();
builder.Services.AddScoped<NotificationPreferenceService>();
builder.Services.AddHttpClient("SendGridClient");
builder.Services.AddHttpClient("MailgunClient");

// Email provider selection: set Email:Provider to "Smtp", "SendGrid", "Mailgun", or "Console"
var emailProvider = builder.Configuration["Email:Provider"] ?? "Console";
switch (emailProvider)
{
    case "Smtp":
        builder.Services.AddScoped<IEmailService, SmtpEmailService>();
        break;
    case "SendGrid":
        builder.Services.AddScoped<IEmailService, SendGridEmailService>();
        break;
    case "Mailgun":
        builder.Services.AddScoped<IEmailService, MailgunEmailService>();
        break;
    default:
        builder.Services.AddScoped<IEmailService, ConsoleEmailService>();
        break;
}

builder.Services.AddScoped<ConsoleEmailService>(); // kept for fallback injection

// ── Helpers ───────────────────────────────────────────────────────────────
builder.Services.AddScoped<CurrentUserProvider>();
builder.Services.AddScoped<JwtHelper>();
builder.Services.AddScoped<IOcrProvider, MockOcrProvider>();

// ── Quartz Scheduler ──────────────────────────────────────────────────────
builder.Services.AddQuartz(q =>
{
    // OCR retry every 30 min
    var ocrKey = new JobKey("OcrRetryJob");
    q.AddJob<OcrRetryJob>(o => o.WithIdentity(ocrKey));
    q.AddTrigger(o => o.ForJob(ocrKey).WithIdentity("ocr-trigger")
        .WithCronSchedule("0 0/30 * * * ?"));

    // Subscription expiry daily 00:05
    var subKey = new JobKey("SubscriptionExpiryJob");
    q.AddJob<SubscriptionExpiryJob>(o => o.WithIdentity(subKey));
    q.AddTrigger(o => o.ForJob(subKey).WithIdentity("sub-trigger")
        .WithCronSchedule("0 5 0 * * ?"));


    // Bulk OCR processing every 2 minutes
    var bulkOcrKey = new JobKey("BulkOcrProcessingJob");
    q.AddJob<BulkOcrProcessingJob>(o => o.WithIdentity(bulkOcrKey));
    q.AddTrigger(o => o.ForJob(bulkOcrKey).WithIdentity("bulkocr-trigger")
        .WithCronSchedule("0 0/2 * * * ?"));

    // Approval escalation every hour
    var escalationKey = new JobKey("ApprovalEscalationJob");
    q.AddJob<ApprovalEscalationJob>(o => o.WithIdentity(escalationKey));
    q.AddTrigger(o => o.ForJob(escalationKey).WithIdentity("escalation-trigger")
        .WithCronSchedule("0 0 * * * ?"));


    // Overdue task notifications daily 09:00
    var overdueKey = new JobKey("OverdueTaskNotificationJob");
    q.AddJob<OverdueTaskNotificationJob>(o => o.WithIdentity(overdueKey));
    q.AddTrigger(o => o.ForJob(overdueKey).WithIdentity("overdue-task-trigger")
        .WithCronSchedule("0 0 9 * * ?"));

    // Webhook delivery every 5 minutes
    var webhookKey = new JobKey("WebhookRetryJob");
    q.AddJob<WebhookRetryJob>(o => o.WithIdentity(webhookKey));
    q.AddTrigger(o => o.ForJob(webhookKey).WithIdentity("webhook-trigger")
        .WithCronSchedule("0 0/5 * * * ?"));

    // Due invoice reminders daily at 08:00 UTC
    var reminderKey = new JobKey("DueInvoiceReminderJob");
    q.AddJob<DueInvoiceReminderJob>(o => o.WithIdentity(reminderKey));
    q.AddTrigger(o => o.ForJob(reminderKey).WithIdentity("reminder-trigger")
        .WithCronSchedule("0 0 8 * * ?"));


    // Weekly digest every Monday 08:00 UTC
    var digestKey = new JobKey("WeeklyDigestJob");
    q.AddJob<WeeklyDigestJob>(o => o.WithIdentity(digestKey));
    q.AddTrigger(o => o.ForJob(digestKey).WithIdentity("digest-trigger")
        .WithCronSchedule("0 0 8 ? * MON"));


    // Data retention — 1st of every month at 03:00 UTC
    var retentionKey = new JobKey("DataRetentionJob");
    q.AddJob<DataRetentionJob>(o => o.WithIdentity(retentionKey));
    q.AddTrigger(o => o.ForJob(retentionKey).WithIdentity("retention-trigger")
        .WithCronSchedule("0 0 3 1 * ?"));

    // Token cleanup daily 02:00
    var cleanKey = new JobKey("TokenCleanupJob");
    q.AddJob<TokenCleanupJob>(o => o.WithIdentity(cleanKey));
    q.AddTrigger(o => o.ForJob(cleanKey).WithIdentity("cleanup-trigger")
        .WithCronSchedule("0 0 2 * * ?"));
});
builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

// ── Controllers & Swagger ─────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title       = "OCR Invoice SaaS API",
        Version     = "v1",
        Description = "Multi-tenant SaaS — JWT + Refresh Tokens + 2FA + API Keys"
    });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Bearer: Enter  Bearer {token}",
        Name = "Authorization", In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey, Scheme = "Bearer"
    });
    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Description = "Machine-to-machine API Key",
        Name = "X-Api-Key", In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, Array.Empty<string>() },
        { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKey"  } }, Array.Empty<string>() }
    });
});

builder.Services.AddCors(o =>
    o.AddPolicy("AllowAll", p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// ── Middleware pipeline ───────────────────────────────────────────────────
app.UseMiddleware<ExceptionMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<TenantContextMiddleware>();
app.UseMiddleware<ApiKeyMiddleware>();          // must be before UseAuthentication
app.UseMiddleware<RateLimitMiddleware>();        // after auth so company context is available
app.UseMiddleware<IpAllowlistMiddleware>();      // per-company IP enforcement
app.UseMiddleware<IdempotencyMiddleware>();      // POST/PATCH idempotency replay
app.UseMiddleware<AuditMiddleware>();            // auto-log all authenticated mutations

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// ── Seed ──────────────────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await DataSeeder.SeedAsync(db);
}

app.Run();
