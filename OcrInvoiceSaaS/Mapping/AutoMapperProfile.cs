using AutoMapper;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Models;

namespace OcrInvoiceSaaS.Mapping;

public class AutoMapperProfile : Profile
{
    public AutoMapperProfile()
    {
        // ── Auth / User ──────────────────────────────────────────────
        CreateMap<User, UserProfileResponse>();

        // ── Company ──────────────────────────────────────────────────
        CreateMap<Company, CompanyResponse>()
            .ForMember(dest => dest.UserRole, opt => opt.Ignore());

        // ── Vendor ───────────────────────────────────────────────────
        CreateMap<Vendor, VendorResponse>();

        // ── Invoice ──────────────────────────────────────────────────
        CreateMap<Invoice, InvoiceResponse>()
            .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.Status.ToString()))
            .ForMember(dest => dest.UploadedByName, opt => opt.MapFrom(src =>
                src.UploadedByUser != null ? src.UploadedByUser.FullName : string.Empty))
            .ForMember(dest => dest.Vendor, opt => opt.MapFrom(src => src.Vendor))
            .ForMember(dest => dest.Items, opt => opt.MapFrom(src => src.InvoiceItems));

        // ── InvoiceItem ───────────────────────────────────────────────
        CreateMap<InvoiceItem, InvoiceItemResponse>();

        // ── ExpenseCategory ───────────────────────────────────────────
        CreateMap<ExpenseCategory, ExpenseCategoryResponse>();

        // ── SubscriptionPlan ──────────────────────────────────────────
        CreateMap<SubscriptionPlan, SubscriptionPlanResponse>();

        // ── CompanySubscription ───────────────────────────────────────
        CreateMap<CompanySubscription, CompanySubscriptionResponse>()
            .ForMember(dest => dest.Plan, opt => opt.MapFrom(src => src.SubscriptionPlan));

        // ── Payment ───────────────────────────────────────────────────
        CreateMap<Payment, PaymentResponse>();
    }
}
