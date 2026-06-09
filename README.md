# OCR Invoice SaaS — Backend (Complete)

ASP.NET Core 8 · SQL Server · EF Core · JWT · AutoMapper · Quartz.NET

---

## Quick Start

```bash
# 1. Set connection string + JWT secret in appsettings.json
# 2. Run — migrations are applied automatically at startup
dotnet run
# Swagger UI → https://localhost:7150/swagger
# Health check → https://localhost:7150/health
```

---

## All Endpoints

### Auth `/api/auth`
| POST `/register` | POST `/login` | GET `/me` ✓ |

### Companies `/api/companies`
| POST `/` | GET `/` | GET `/{id}` |

### Members `/api/companies/{id}/members`
| GET `/` | POST `/` (Owner/Admin) | DELETE `/{userId}` (Owner/Admin) |

### Vendors `/api/companies/{id}/vendors`
| POST `/` | GET `/` |

### Invoices `/api/companies/{id}/invoices`
| POST `/` | GET `/` (paginated+filtered) | GET `/{id}` | PATCH `/{id}` | POST `/{id}/ocr` |

**GET query params:** `page`, `pageSize`, `search`, `status`, `sortBy`, `sortDir`

### Invoice Items `/api/invoices/{id}/items`
| POST `/` (auto-recalcs totals) | PATCH `/{itemId}` | DELETE `/{itemId}` |

### Expense Categories `/api/companies/{id}/expense-categories`
| POST `/` | GET `/` | DELETE `/{categoryId}` |

### Dashboard `/api/companies/{id}/dashboard`
| GET `/` — totals, 12-month summary, recent invoices |

### Subscriptions
| GET `/api/subscription-plans` | POST/GET `/api/companies/{id}/subscription` | POST `/api/subscriptions/{id}/payments` |

### Notifications `/api/notifications`
| GET `/?unreadOnly=true` | POST `/mark-read` |

### Files `/api/files`
| POST `/presign` — S3 pre-signed URL | POST `/upload` — direct multipart (10 MB max) |

### Audit `/api/audit/{entityType}/{entityId}`
| GET full audit trail for any entity |

### Duplicate Detection
| GET `/api/companies/{id}/duplicates` | GET `/api/invoices/{id}/duplicates` | POST `/api/companies/{id}/invoices/{id}/check-duplicates` | POST `/api/duplicates/{flagId}/review` |

Runs automatically after OCR; flags ≥60% match on invoice number / vendor / amount / date.

### Approval Workflows
| POST/GET `/api/companies/{id}/workflow-templates` | DELETE `/api/workflow-templates/{id}` (Owner/Admin) |
| POST `/api/invoices/{id}/approvals` | GET `/api/approvals/pending` | GET `/api/approvals/{id}` | POST `/api/approvals/{id}/actions` | POST `/api/approvals/{id}/cancel` (Owner/Admin) |

Steps assigned by user or role; optional steps auto-skip on timeout (ApprovalEscalationJob, hourly).

### Bulk OCR
| POST `/api/companies/{id}/bulk-ocr` (≤100 invoices) | GET `/api/companies/{id}/bulk-ocr` | GET `/api/bulk-ocr/{jobId}` | POST `/api/bulk-ocr/{jobId}/cancel` |

Queue drained in batches of 10 by BulkOcrProcessingJob every 2 minutes.

### Purchase Orders & Matching
| POST/GET `/api/companies/{id}/purchase-orders` | GET/DELETE `/api/purchase-orders/{id}` |
| POST `/api/invoices/{id}/po-matches/auto` | POST/GET `/api/invoices/{id}/po-matches` | POST `/api/po-matches/{id}/dismiss` |

Auto-match scores vendor (40) + amount (≤40) + PO number in OCR text (20); ≥80 = Matched.

### Currency Conversion
| GET `/api/currency/rate?from=&to=` | GET `/api/currency/convert?from=&to=&amount=` | GET `/api/currency/rates?base=GBP` | POST `/api/invoices/{id}/exchange-rate/refresh` |

Open Exchange Rates with DB cache (`Currency:CacheHours`); base-currency amount attached to invoices automatically after OCR.

### Webhooks
| POST/GET `/api/companies/{id}/webhooks` (Owner/Admin to create) | DELETE `/api/webhooks/{id}` | GET `/api/webhooks/{id}/deliveries` | POST `/api/webhook-deliveries/{id}/retry` |

Events: `InvoiceCreated, InvoiceOcrCompleted, InvoiceStatusChanged, InvoiceApproved, InvoiceRejected, DuplicateFlagged, BulkOcrCompleted, PaymentRecorded, ApprovalRequested` (empty list = all).
Delivery via WebhookRetryJob (5 min) with exponential backoff. Verify signatures: `X-Webhook-Signature: t=<ts>,v1=<hex>` where `v1 = HMAC-SHA256(key = SHA-256-hex(signingSecret), msg = "<t>.<body>")`.

### Reports `/api/companies/{id}/reports`
| GET `/spend-analytics` | GET `/vat-summary?periodStart=&periodEnd=` | GET `/export/csv` | GET `/export/excel` |

### Health `/health`
| GET — DB connectivity check |

---

## Invoice Status Flow

```
Uploaded → Processing → Processed → Reviewed → Approved
                └── Failed (retried by OcrRetryJob every 30 min)
```

---

## Roles

| Role | View | Upload | Manage Members | Manage Subscriptions |
|------|------|--------|----------------|----------------------|
| Owner | ✓ | ✓ | ✓ | ✓ |
| Admin | ✓ | ✓ | ✓ (not owners) | ✓ |
| Member | ✓ | ✓ | ✗ | ✗ |

---

## Seeding

Add to `Program.cs` before `app.Run()`:
```csharp
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await DataSeeder.SeedAsync(db); // seeds Starter/Growth/Enterprise plans
}
```

---

## Swap OCR Provider

In `Program.cs` replace:
```csharp
builder.Services.AddScoped<IOcrProvider, MockOcrProvider>();
// with:
builder.Services.AddScoped<IOcrProvider, AwsTextractProvider>(); // or GcpVisionProvider
```
