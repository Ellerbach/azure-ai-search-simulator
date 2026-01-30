# Entra ID Authentication - TODO List

> **Related Document:** [ENTRA-ID-AUTHENTICATION-PLAN.md](ENTRA-ID-AUTHENTICATION-PLAN.md)

## Overview

This checklist tracks the implementation progress of Entra ID authentication for the Azure AI Search Simulator.

---

## Phase 1: Foundation (Week 1) ✅ COMPLETED

### Configuration Models

- [x] Create `AuthenticationSettings.cs` in `Core/Configuration/`
- [x] Create `ApiKeySettings.cs` class
- [x] Create `EntraIdSettings.cs` class
- [x] Create `SimulatedAuthSettings.cs` class
- [x] Create `RoleMappingSettings.cs` class with all 6 roles:
  - [x] Owner (`8e3af657-a8ff-443c-a75c-2fe8c4bcb635`)
  - [x] Contributor (`b24988ac-6180-42a0-ab88-20f7382dd24c`)
  - [x] Reader (`acdd72a7-3385-48ef-bd42-f606fba81ae7`)
  - [x] Search Service Contributor (`7ca78c08-252a-4471-8644-bb5ff32d4ba0`)
  - [x] Search Index Data Contributor (`8ebe5a00-799e-43f5-93ac-243d3dce84a7`)
  - [x] Search Index Data Reader (`1407120a-92aa-4202-b7e9-c0e197c71c8f`)

### Authentication Interfaces

- [x] Create `IAuthenticationHandler.cs` interface
- [x] Create `AuthenticationResult.cs` model
- [x] Create `AccessLevel` enum matching Azure RBAC model

### API Key Handler Refactor

- [x] Extract logic from `ApiKeyAuthenticationMiddleware.cs` into `ApiKeyAuthenticationHandler.cs`
- [x] Implement `IAuthenticationHandler` interface
- [x] Support both `api-key` header and query parameter

### Unified Middleware

- [x] Create `AuthenticationMiddleware.cs` (new unified middleware)
- [x] Implement handler chain pattern
- [x] Implement API key precedence rule (matches Azure behavior)
- [x] Update `Program.cs` DI registration
- [x] Update `appsettings.json` with Authentication section

### Unit Tests

- [x] Create `ApiKeyAuthenticationHandlerTests.cs` (19 tests)
- [x] Create `AccessLevelTests.cs` (8 test categories)
- [x] Create `AuthenticationResultTests.cs` (10 tests)
- [x] Create `AuthenticationMiddlewareTests.cs` (12 tests)
- [x] Test valid admin key
- [x] Test valid query key
- [x] Test invalid key rejection
- [x] Test missing key rejection
- [x] Test API key precedence behavior

---

## Phase 2: Simulated Entra ID (Week 2)

### Simulated Token Handler

- [ ] Create `SimulatedAuthenticationHandler.cs`
- [ ] Implement `IAuthenticationHandler` interface
- [ ] Validate JWT signature with configured signing key
- [ ] Extract and validate claims (aud, iss, exp, nbf, roles)
- [ ] Map roles to `AccessLevel`

### Token Generator Service

- [ ] Create `SimulatedTokenService.cs`
- [ ] Generate JWT tokens with Entra ID-like structure
- [ ] Support service principal tokens (`idtyp: app`)
- [ ] Support user tokens (`idtyp: user`)
- [ ] Support configurable roles and expiration

### Token Controller

- [ ] Create `TokenController.cs`
- [ ] `POST /admin/token` - Generate test tokens
- [ ] `POST /admin/token/validate` - Validate any token

### Authorization Service

- [ ] Create `AuthorizationService.cs`
- [ ] Implement permission matrix logic
- [ ] Map operations to required roles
- [ ] Check role membership for authorization

### Role-Based Access Control

- [ ] Implement authorization checks in controllers
- [ ] Index operations require `Search Service Contributor`
- [ ] Document operations require `Search Index Data Contributor`
- [ ] Query operations require `Search Index Data Reader`
- [ ] Return 403 Forbidden for insufficient permissions

### Integration Tests

- [ ] Create `SimulatedAuthenticationTests.cs`
- [ ] Test token generation
- [ ] Test token validation
- [ ] Test RBAC enforcement (success cases)
- [ ] Test RBAC enforcement (failure cases)

### Sample Updates

- [ ] Update `samples/entra-id-auth-requests.http` with working examples

---

## Phase 3: Real Entra ID (Week 3)

### NuGet Packages

- [ ] Add `Microsoft.Identity.Web` package
- [ ] Add `Microsoft.AspNetCore.Authentication.JwtBearer` package
- [ ] Add `System.IdentityModel.Tokens.Jwt` package

### Entra ID Handler

- [ ] Create `EntraIdAuthenticationHandler.cs`
- [ ] Implement `IAuthenticationHandler` interface
- [ ] Configure JWT bearer authentication
- [ ] Validate tokens against Azure AD metadata endpoint
- [ ] Support multiple valid audiences (sovereign clouds)
- [ ] Support multiple valid issuers

### Claims Extraction

- [ ] Extract `aud` (audience)
- [ ] Extract `iss` (issuer)
- [ ] Extract `oid` (object ID)
- [ ] Extract `tid` (tenant ID)
- [ ] Extract `appid` / `azp` (application ID)
- [ ] Extract `roles` (app roles)
- [ ] Extract `scp` (scopes for delegated tokens)
- [ ] Extract `idtyp` (identity type)
- [ ] Extract `name` / `preferred_username`

### Multi-Tenant Support

- [ ] Configure tenant validation
- [ ] Support single-tenant mode
- [ ] Support multi-tenant mode (optional)

### Testing with Real Azure

- [ ] Create Azure AD app registration for testing
- [ ] Configure app roles in Azure AD
- [ ] Test with real Azure AD tokens
- [ ] Document app registration steps

### Unit Tests

- [ ] Create `EntraIdAuthenticationHandlerTests.cs`
- [ ] Test valid token acceptance
- [ ] Test expired token rejection
- [ ] Test invalid audience rejection
- [ ] Test invalid issuer rejection
- [ ] Test role extraction

---

## Phase 4: Outbound Authentication (Week 4)

### Configuration

- [ ] Create `OutboundAuthenticationSettings.cs`
- [ ] Add `DefaultCredentialSettings` class
- [ ] Add `ServicePrincipalSettings` class
- [ ] Add `TokenCacheSettings` class

### Credential Factory

- [ ] Create `ICredentialFactory.cs` interface
- [ ] Create `CredentialFactory.cs` implementation
- [ ] Implement `DefaultAzureCredential` mode
- [ ] Implement `ServicePrincipal` mode
- [ ] Implement `ManagedIdentity` mode
- [ ] Add credential caching

### Update Data Source Connectors

- [ ] Inject `ICredentialFactory` into `AzureBlobStorageConnector`
- [ ] Inject `ICredentialFactory` into `AdlsGen2Connector`
- [ ] Use factory instead of inline credential creation
- [ ] Add detailed logging for credential resolution

### Diagnostics Endpoint

- [ ] Create `DiagnosticsController.cs`
- [ ] `GET /diagnostics/auth` - Show auth configuration
- [ ] `GET /diagnostics/credentials` - Test outbound credentials
- [ ] Show which credential type is being used
- [ ] Test token acquisition

### Testing

- [ ] Test with Azure CLI credentials
- [ ] Test with environment variables
- [ ] Test with managed identity (if available)
- [ ] Test credential caching

---

## Phase 5: Resource-Level Identity (Week 5)

### Common Identity Model

- [ ] Create `SearchIdentity.cs` model
- [ ] Create `SearchIdentityTypes` constants class
- [ ] Support `#Microsoft.Azure.Search.DataNone`
- [ ] Support `#Microsoft.Azure.Search.DataUserAssignedIdentity`

### Custom Skill Authentication

- [ ] Add `AuthResourceId` property to `Skill` model
- [ ] Add `AuthIdentity` property to `Skill` model
- [ ] Update `CustomWebApiSkillExecutor` to acquire tokens
- [ ] Implement `NormalizeToScope()` helper method
- [ ] Pass token in `Authorization: Bearer` header to skill endpoint

### Skillset Cognitive Services Identity

- [ ] Create/update `CognitiveServicesAccount.cs` model
- [ ] Support `#Microsoft.Azure.Search.CognitiveServicesByKey`
- [ ] Support `#Microsoft.Azure.Search.AIServicesByKey`
- [ ] Support `#Microsoft.Azure.Search.AIServicesByIdentity`
- [ ] Add `SubdomainUrl` property
- [ ] Add `Identity` property

### Data Source Identity

- [ ] Add `Identity` property to `DataSource` model
- [ ] Update blob connector to use identity when specified
- [ ] Update ADLS connector to use identity when specified
- [ ] Support system-assigned identity (null)
- [ ] Support user-assigned identity

### Indexer Identity

- [ ] Add `Identity` property to `Indexer` model
- [ ] Use indexer identity for data source connections
- [ ] Use indexer identity for custom skill authentication

### Integration Tests

- [ ] Test custom skill with `authResourceId`
- [ ] Test data source with managed identity
- [ ] Test indexer with managed identity

---

## Phase 6: Documentation & Polish

### Documentation

- [ ] Update `README.md` with authentication section
- [ ] Update `docs/CONFIGURATION.md` with auth settings
- [ ] Update `docs/API-REFERENCE.md` with auth headers
- [ ] Create `docs/AUTHENTICATION.md` comprehensive guide
- [ ] Create `samples/entra-id-setup.md` Azure AD setup guide

### Error Messages

- [ ] Improve 401 Unauthorized messages
- [ ] Improve 403 Forbidden messages
- [ ] Include authentication mode in error responses
- [ ] Include helpful troubleshooting hints

### Logging

- [ ] Log authentication attempts (success/failure)
- [ ] Log identity information (without sensitive data)
- [ ] Log role/permission checks
- [ ] Add correlation IDs for tracing

### Configuration Validation

- [ ] Validate auth settings at startup
- [ ] Check for missing required settings
- [ ] Warn about insecure configurations (simulated mode in prod)

---

## Success Criteria Checklist

### Functional Requirements

- [x] API key authentication works (backward compatible)
- [ ] Simulated tokens can be generated and validated locally
- [ ] Real Entra ID tokens are validated correctly
- [x] Multiple auth modes can be enabled simultaneously
- [ ] Role-based access control works across all modes
- [ ] Outbound credentials use configurable DefaultAzureCredential
- [ ] Data source connectors support managed identity
- [ ] Custom WebApiSkill supports `authResourceId` and `authIdentity`
- [ ] Custom skills can authenticate to protected endpoints
- [ ] Skillsets support `cognitiveServices` with identity-based auth
- [ ] Data sources support `identity` property
- [ ] Indexers support `identity` property
- [ ] SearchIdentity model handles system and user-assigned identities

### Non-Functional Requirements

- [ ] Authentication adds < 10ms latency per request
- [x] Token validation failures provide helpful error messages
- [ ] Configuration errors are caught at startup
- [x] All authentication events are properly logged
- [x] Unit test coverage > 80% for auth components (102 tests)

### Compatibility Requirements

- [x] Azure SDK (Azure.Search.Documents) works with all auth modes
- [x] Existing HTTP samples work without changes (API key)
- [ ] Docker deployment supports all auth configurations

---

## Quick Reference: Files to Create/Modify

### New Files

| File | Description | Status |
| ---- | ----------- | ------ |
| `Core/Configuration/AuthenticationSettings.cs` | Main auth config | ✅ Created |
| `Core/Services/Authentication/IAuthenticationHandler.cs` | Handler interface | ✅ Created |
| `Core/Services/Authentication/AuthenticationResult.cs` | Auth result model | ✅ Created |
| `Core/Services/Authentication/AccessLevel.cs` | Access level enum | ✅ Created |
| `Api/Services/Authentication/ApiKeyAuthenticationHandler.cs` | API key handler | ✅ Created |
| `Api/Middleware/AuthenticationMiddleware.cs` | Unified auth middleware | ✅ Created |
| `Core/Services/Authentication/ICredentialFactory.cs` | Credential factory interface | ⏳ Phase 4 |
| `Core/Services/Authentication/CredentialFactory.cs` | Credential factory impl | ⏳ Phase 4 |
| `Core/Configuration/OutboundAuthenticationSettings.cs` | Outbound auth config | ⏳ Phase 4 |
| `Core/Models/SearchIdentity.cs` | Identity model | ⏳ Phase 5 |
| `Core/Models/CognitiveServicesAccount.cs` | Cognitive services config | ⏳ Phase 5 |
| `Api/Services/Authentication/EntraIdAuthenticationHandler.cs` | Entra ID handler | ⏳ Phase 3 |
| `Api/Services/Authentication/SimulatedAuthenticationHandler.cs` | Simulated handler | ⏳ Phase 2 |
| `Api/Services/Authentication/SimulatedTokenService.cs` | Token generator | ⏳ Phase 2 |
| `Api/Services/Authorization/AuthorizationService.cs` | Authorization logic | ⏳ Phase 2 |
| `Api/Controllers/TokenController.cs` | Token endpoints | ⏳ Phase 2 |
| `Api/Controllers/DiagnosticsController.cs` | Diagnostics endpoints | ⏳ Phase 4 |

### Files Modified (Phase 1)

| File | Changes | Status |
| ---- | ------- | ------ |
| `Api/Program.cs` | Update DI registration | ✅ Modified |
| `Api/appsettings.json` | Add Authentication section | ✅ Modified |
| `Core/AzureAISearchSimulator.Core.csproj` | Add AspNetCore framework ref | ✅ Modified |
| `Api.Tests/AzureAISearchSimulator.Api.Tests.csproj` | Add test packages | ✅ Modified |

### Files to Modify (Future Phases)

| File | Changes | Phase |
| ---- | ------- | ----- |
| `Core/Models/Skillset.cs` | Add authResourceId, authIdentity | Phase 5 |
| `Core/Models/DataSource.cs` | Add identity property | Phase 5 |
| `Core/Models/Indexer.cs` | Add identity property | Phase 5 |
| `DataSources/AzureBlobStorageConnector.cs` | Use credential factory | Phase 4 |
| `DataSources/AdlsGen2Connector.cs` | Use credential factory | Phase 4 |
| `Search/Skills/CustomWebApiSkillExecutor.cs` | Add token acquisition | Phase 5 |

---

## Notes

- Start with Phase 1 to establish the foundation without breaking existing functionality
- Phase 2 (Simulated) can be used for all testing without Azure dependency
- Phase 3 (Real Entra ID) requires Azure AD app registration
- Phases 4 & 5 are for outbound/resource-level auth and can be done in parallel
