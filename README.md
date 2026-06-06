# OCR Invoice SaaS — Backend (Complete)

ASP.NET Core 8 · SQL Server · EF Core · JWT · AutoMapper · Quartz.NET

---

## Quick Start

```bash
# 1. Set connection string + JWT secret in appsettings.json
# 2. Run migrations
dotnet ef migrations add InitialCreate --output-dir Migrations
dotnet ef database update
# 3. Run
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
