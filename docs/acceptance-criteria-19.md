# Acceptance Criteria — Issue #19

**Bug:** `[Cache]` attribute ignores MVC `JsonSerializerOptions`, always serializes to PascalCase  
**Fix version:** v1.4.2 (patch)  
**File:** `BoricuaCoder.API.CoreSetup/Caching/CacheAttribute.cs`

---

## AC-1: MVC respeta `AddJsonOptions`

```
Given: Una API MVC con `builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase)`
And: Un endpoint con `[Cache(60)]` que retorna `new { UserId = 1, FirstName = "Ana" }`
When: Se hace la primera request (cache miss)
Then: La respuesta HTTP tiene `{ "userId": 1, "firstName": "Ana" }` (camelCase)

When: Se hace la segunda request (cache hit desde Redis)
Then: La respuesta HTTP sigue siendo `{ "userId": 1, "firstName": "Ana" }` (camelCase)
And: El JSON almacenado en Redis es `{ "userId": 1, "firstName": "Ana" }` (camelCase)
```

---

## AC-2: Minimal API respeta `ConfigureHttpJsonOptions`

```
Given: Una Minimal API con `builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase)`
And: Un endpoint con `.AddEndpointFilter(new CacheAttribute(60))` que retorna `Results.Ok(new { ProductId = 5, ProductName = "Widget" })`
When: Se hace la primera request (cache miss)
Then: La respuesta tiene `{ "productId": 5, "productName": "Widget" }` (camelCase)

When: Se hace la segunda request (cache hit)
Then: La respuesta sigue siendo `{ "productId": 5, "productName": "Widget" }` (camelCase)
```

---

## AC-3: Fallback seguro cuando no hay `JsonOptions` configurado

```
Given: Una API que NO llama `AddJsonOptions` ni `ConfigureHttpJsonOptions`
When: El `[Cache]` attribute intenta serializar una respuesta
Then: No se lanza ninguna excepción
And: El JSON es serializado usando `JsonSerializerOptions.Default` (comportamiento anterior)
```

---

## AC-4: Converters custom son respetados

```
Given: Una API con un JsonConverter custom registrado en `JsonOptions` (e.g., `DateOnly` a `"yyyy-MM-dd"`)
And: Un endpoint cacheado que retorna un objeto con propiedades `DateOnly`
When: Se sirve desde cache (hit)
Then: El formato de fecha en la respuesta es `"yyyy-MM-dd"`, no el formato default de System.Text.Json
```

---

## AC-5: No regresión — non-OK results no se cachean

```
Given: Un endpoint con `[Cache(60)]` que retorna `NotFound()`
When: Se ejecuta el endpoint
Then: Nada se escribe en Redis
And: La respuesta HTTP es 404
```

---

## Sitios adicionales identificados (fuera del scope original del issue)

- `Caching/RedisCacheService.cs` línea 45 — `SetAsync<T>` serializa sin options
- `Caching/RedisCacheService.cs` línea 33 — `GetAsync<T>` deserializa sin options
