---
name: csharp-file-layout
description: >-
  Enforces C# file layout: namespace declaration first, then using directives.
  System.* usings come first, then remaining usings in alphabetical order.
  Use when creating or editing .cs files, adding usings, or organizing C# namespaces.
---

# C# file layout (namespace + usings)

When creating or editing `.cs` files, follow this order strictly.

## Order in every file

1. **`namespace` first** (file-scoped preferred)
2. **Then `using` directives**
3. Then blank line
4. Then types

```csharp
namespace AnalyticsApi.Services.GeoIp;

using System.Net;
using AnalyticsApi.Contracts.Responses;
using Microsoft.Extensions.Caching.Memory;

public sealed class GeoIpService
{
}
```

Do **not** put usings above the namespace.

## Using sort order

1. All `System` / `System.*` usings first, alphabetically among themselves
2. Then every other using, alphabetically (case-insensitive)
3. One using per line
4. No blank line between System and non-System groups unless the project already does that — default: **no blank line** between groups
5. Remove unused usings

### System group examples (sorted)

```csharp
using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
```

### Then non-System (sorted)

```csharp
using AnalyticsApi.Contracts;
using AnalyticsApi.Contracts.Responses;
using AnalyticsApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
```

## Full example

```csharp
namespace AnalyticsApi.Controllers;

using System.Threading;
using AnalyticsApi.Contracts;
using AnalyticsApi.Services;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

[ApiController]
[Route("api/analytics")]
public sealed class AnalyticsController
{
}
```

## Apply when

- Adding a new `.cs` file
- Moving types between folders/namespaces
- Touching usings in an existing file (reorder to match this skill)
- Reviewing or refactoring C# layout

## Do not

- Put `using` before `namespace`
- Sort all usings in one alphabet ignoring System-first
- Leave unused usings after edits
