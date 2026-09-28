# Alvys Public API boundary

This portfolio integration was authored only from public documentation and generic HTTP/OAuth patterns. It does not reuse former-employer DTOs, internal connector code, private API routes, tenant identifiers, or observed undocumented write behavior.

## Public sources

- Authentication: https://alvys.readme.io/reference/authentication
- Search loads: https://docs.alvys.com/en/api/reference/loads/search-loads
- Search trailers: https://docs.alvys.com/reference/post_api-p-v-version-trailers-search
- Rate limits: https://docs.alvys.com/docs/rate-limits

## Implemented behavior

### OAuth 2.0 client credentials

The server posts its client id and client secret to:

```text
https://auth.alvys.com/oauth/token
```

with the public API audience:

```text
https://api.alvys.com/public/
```

The access token remains in API process memory and is never returned to the browser.

### Loads Search

Freight Ops and LTL Planner use the documented read endpoint:

```text
POST https://integrations.alvys.com/api/p/v1/loads/search
```

The portfolio requests only documented read fields and normalizes the response into its own DTOs.

### Trailers Search

Yard Ops uses the documented read endpoint:

```text
POST https://integrations.alvys.com/api/p/v1/trailers/search
```

The portfolio maps trailer number, equipment type/size, fleet and status into its own external-equipment DTO.

## Intentionally excluded

- undocumented/internal endpoints
- copied private schemas
- writes back to Alvys
- employer-specific field mappings
- tenant-specific IDs
- credentials of any kind
