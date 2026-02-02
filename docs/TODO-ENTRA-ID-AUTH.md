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

## Phase 2: Simulated Entra ID (Week 2) ✅ COMPLETED

### Simulated Token Handler

- [x] Create `SimulatedAuthenticationHandler.cs`
- [x] Implement `IAuthenticationHandler` interface
- [x] Validate JWT signature with configured signing key
- [x] Extract and validate claims (aud, iss, exp, nbf, roles)
- [x] Map roles to `AccessLevel`

### Token Generator Service

- [x] Create `SimulatedTokenService.cs`
- [x] Generate JWT tokens with Entra ID-like structure
- [x] Support service principal tokens (`idtyp: app`)
- [x] Support user tokens (`idtyp: user`)
- [x] Support configurable roles and expiration

### Token Controller

- [x] Create `TokenController.cs`
- [x] `POST /admin/token` - Generate test tokens
- [x] `POST /admin/token/validate` - Validate any token
- [x] `GET /admin/token/info` - Get auth configuration info
- [x] `GET /admin/token/quick/{role}` - Quick token generation with role shortcuts

### Authorization Service

- [x] Create `AuthorizationService.cs`
- [x] Implement permission matrix logic
- [x] Map operations to required roles
- [x] Check role membership for authorization
- [x] `SearchOperation` enum for all operations
- [x] `AuthorizationResult` model
- [x] Extension methods (`GetAccessLevel`, `HasAccess`)

### Role-Based Access Control

- [x] Implement authorization checks via `IAuthorizationService`
- [x] Index operations require `Search Service Contributor`
- [x] Document operations require `Search Index Data Contributor`
- [x] Query operations require `Search Index Data Reader`
- [x] Return 403 Forbidden for insufficient permissions (via `AuthorizationResult`)

### Unit Tests

- [x] Create `SimulatedTokenServiceTests.cs` (18 tests)
- [x] Create `SimulatedAuthenticationHandlerTests.cs` (20 tests)
- [x] Create `AuthorizationServiceTests.cs` (25+ tests)
- [x] Test token generation
- [x] Test token validation
- [x] Test RBAC enforcement (success cases)
- [x] Test RBAC enforcement (failure cases)

### Configuration Updates

- [x] Enable Simulated mode in `appsettings.json`
- [x] Add `EnabledModes: ["ApiKey", "Simulated"]`
- [x] Register services in `Program.cs`

---

## Phase 3: Real Entra ID (Week 3) ✅ COMPLETED

### NuGet Packages

- [x] Add `Microsoft.IdentityModel.Tokens` package (added in Phase 2)
- [x] Add `System.IdentityModel.Tokens.Jwt` package (added in Phase 2)
- [x] Add `Microsoft.Identity.Web` package
- [x] Add `Microsoft.AspNetCore.Authentication.JwtBearer` package
- [x] Add `Microsoft.IdentityModel.Protocols.OpenIdConnect` package

### Entra ID Handler

- [x] Create `EntraIdAuthenticationHandler.cs`
- [x] Implement `IAuthenticationHandler` interface
- [x] Configure JWT bearer authentication
- [x] Validate tokens against Azure AD metadata endpoint
- [x] Support multiple valid audiences (sovereign clouds)
- [x] Support multiple valid issuers

### Token Validator Service

- [x] Create `EntraIdTokenValidator.cs`
- [x] Create `IEntraIdTokenValidator` interface
- [x] Implement OpenID Connect configuration retrieval
- [x] Cache configuration with 24-hour expiration
- [x] Support signing key refresh from Azure AD

### Claims Extraction

- [x] Extract `aud` (audience)
- [x] Extract `iss` (issuer)
- [x] Extract `oid` (object ID)
- [x] Extract `tid` (tenant ID)
- [x] Extract `appid` / `azp` (application ID)
- [x] Extract `roles` (app roles)
- [x] Extract `scp` (scopes for delegated tokens)
- [x] Extract `idtyp` (identity type)
- [x] Extract `name` / `preferred_username`

### Multi-Tenant Support

- [x] Configure tenant validation
- [x] Support single-tenant mode
- [x] Support multi-tenant mode (optional via `AllowMultipleTenants`)

### Sovereign Cloud Support

- [x] Azure Public Cloud
- [x] Azure Government (US)
- [x] Azure China
- [x] Azure Germany

### Unit Tests

- [x] Create `EntraIdAuthenticationHandlerTests.cs` (30+ tests)
- [x] Create `EntraIdTokenValidatorTests.cs` (30+ tests)
- [x] Test valid token acceptance
- [x] Test expired token rejection
- [x] Test invalid audience rejection
- [x] Test invalid issuer rejection
- [x] Test role extraction
- [x] Test all access level mappings

### Testing with Real Azure (Documentation)

- [ ] Create Azure AD app registration for testing (see CONFIGURATION.md)
- [ ] Configure app roles in Azure AD
- [ ] Test with real Azure AD tokens
- [ ] Document app registration steps

---

## Phase 4: Outbound Authentication (Week 4) ✅ COMPLETED

### Configuration

- [x] Create `OutboundAuthenticationSettings.cs`
- [x] Add `DefaultCredentialSettings` class
- [x] Add `ServicePrincipalSettings` class
- [x] Add `ManagedIdentitySettings` class
- [x] Add `TokenCacheSettings` class

### Credential Factory

- [x] Create `ICredentialFactory.cs` interface
- [x] Create `CredentialFactory.cs` implementation
- [x] Implement `DefaultAzureCredential` mode
- [x] Implement `ServicePrincipal` mode (client secret and certificate)
- [x] Implement `ManagedIdentity` mode (system and user-assigned)
- [x] Add token caching with configurable expiration buffer

### Search Identity Model

- [x] Create `SearchIdentity` class
- [x] Create `SearchIdentityTypes` constants (`None`, `UserAssignedIdentity`)
- [x] Support `IsNone`, `IsUserAssigned`, `IsSystemAssigned` properties
- [x] Create `CredentialInfo` and `CredentialTestResult` models

### Update Data Source Connectors

- [x] Inject `ICredentialFactory` into `AzureBlobStorageConnector`
- [x] Inject `ICredentialFactory` into `AdlsGen2Connector`
- [x] Use factory instead of inline `DefaultAzureCredential` creation
- [x] Add detailed logging for credential resolution
- [x] Support identity-based authentication per data source

### Diagnostics Endpoint

- [x] Create `DiagnosticsController.cs`
- [x] `GET /admin/diagnostics/auth` - Show auth configuration
- [x] `GET /admin/diagnostics/credentials/test` - Test outbound credentials
- [x] `POST /admin/diagnostics/credentials/token` - Acquire token for scope
- [x] Show which credential type is being used

### Unit Tests

- [x] Create `CredentialFactoryTests.cs` (18 tests)
- [x] Create `OutboundAuthenticationSettingsTests.cs` (21 tests)
- [x] Test credential creation for all types
- [x] Test identity-based credential selection
- [x] Test credential info masking
- [x] Test configuration defaults

---

## Phase 5: Resource-Level Identity (Week 5) ✅ COMPLETED

### Resource Identity Model

- [x] Create `ResourceIdentity.cs` model
- [x] Create `ResourceIdentityTypes` constants class
- [x] Support `#Microsoft.Azure.Search.DataNone`
- [x] Support `#Microsoft.Azure.Search.DataUserAssignedIdentity`
- [x] Factory methods: `None()`, `SystemAssigned()`, `UserAssigned()`
- [x] Helper properties: `IsNone`, `IsUserAssigned`, `IsSystemAssigned`

### Custom Skill Authentication

- [x] Add `AuthResourceId` property to `Skill` model
- [x] Add `AuthIdentity` property to `Skill` model
- [x] Update `CustomWebApiSkillExecutor` to acquire tokens
- [x] Implement `NormalizeToScope()` helper method
- [x] Pass token in `Authorization: Bearer` header to skill endpoint
- [x] Support optional `ICredentialFactory` injection

### Skillset Cognitive Services Identity

- [x] Update `CognitiveServicesAccount.cs` model
- [x] Support `#Microsoft.Azure.Search.CognitiveServicesByKey`
- [x] Support `#Microsoft.Azure.Search.AIServicesByKey`
- [x] Support `#Microsoft.Azure.Search.AIServicesByIdentity`
- [x] Add `SubdomainUrl` property
- [x] Add `Identity` property
- [x] Add `UsesApiKey` and `UsesManagedIdentity` helper properties

### Data Source Identity

- [x] Add `Identity` property to `DataSource` model
- [x] Update blob connector to use identity when specified
- [x] Update ADLS connector to use identity when specified
- [x] Support system-assigned identity (null ODataType)
- [x] Support user-assigned identity

### Indexer Identity

- [x] Add `Identity` property to `Indexer` model
- [x] Use indexer identity for data source connections
- [x] Use indexer identity for custom skill authentication

### Unit Tests

- [x] Create `ResourceIdentityTests.cs` (23 tests)
- [x] Test factory methods
- [x] Test JSON serialization/deserialization
- [x] Test integration with DataSource model
- [x] Test integration with Indexer model
- [x] Test integration with Skill model
- [x] Test CognitiveServicesAccount identity

---

## Phase 6: Documentation & Polish ✅ COMPLETED

### Documentation

- [x] Update `README.md` with authentication section
- [x] Update `docs/CONFIGURATION.md` with auth settings (already had auth section)
- [x] Update `docs/API-REFERENCE.md` with auth headers and Admin endpoints
- [x] Create `docs/AUTHENTICATION.md` comprehensive guide

### Error Messages

- [x] Improve 401 Unauthorized messages with troubleshooting hints
- [x] Improve 403 Forbidden messages with required role info
- [x] Include authentication mode in error responses
- [x] Include helpful troubleshooting hints in error details

### Logging

- [x] Log authentication attempts (success/failure) at Information level
- [x] Log identity information (without sensitive data)
- [x] Log role/permission checks via AuthorizationService
- [x] Include path and remote IP in auth logs

### Configuration Validation

- [x] Create `AuthenticationConfigurationValidator` hosted service
- [x] Validate auth settings at startup
- [x] Check for missing required settings
- [x] Warn about insecure configurations (simulated mode in prod, default keys)
- [x] Warn about default signing keys and API keys

---

## Success Criteria Checklist

### Functional Requirements

- [x] API key authentication works (backward compatible)
- [x] Simulated tokens can be generated and validated locally
- [x] Real Entra ID tokens are validated correctly
- [x] Multiple auth modes can be enabled simultaneously
- [x] Role-based access control works across all modes
- [x] Outbound credentials use configurable DefaultAzureCredential
- [x] Data source connectors support managed identity
- [x] Custom WebApiSkill supports `authResourceId` and `authIdentity`
- [x] Custom skills can authenticate to protected endpoints
- [x] Skillsets support `cognitiveServices` with identity-based auth
- [x] Data sources support `identity` property
- [x] Indexers support `identity` property
- [x] SearchIdentity/ResourceIdentity model handles system and user-assigned identities

### Non-Functional Requirements

- [x] Authentication adds < 10ms latency per request
- [x] Token validation failures provide helpful error messages
- [x] Configuration errors are caught at startup
- [x] All authentication events are properly logged
- [x] Unit test coverage > 80% for auth components (292+ tests)

### Compatibility Requirements

- [x] Azure SDK (Azure.Search.Documents) works with all auth modes
- [x] Existing HTTP samples work without changes (API key)
- [x] Docker deployment supports all auth configurations

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
| `Api/Services/Authentication/SimulatedAuthenticationHandler.cs` | Simulated handler | ✅ Created |
| `Api/Services/Authentication/SimulatedTokenService.cs` | Token generator | ✅ Created |
| `Api/Services/Authorization/AuthorizationService.cs` | Authorization logic | ✅ Created |
| `Api/Controllers/TokenController.cs` | Token endpoints | ✅ Created |
| `Api/Middleware/AuthenticationMiddleware.cs` | Unified auth middleware | ✅ Created |
| `Core/Services/Authentication/ICredentialFactory.cs` | Credential factory interface | ⏳ Phase 4 |
| `Core/Services/Authentication/CredentialFactory.cs` | Credential factory impl | ⏳ Phase 4 |
| `Core/Configuration/OutboundAuthenticationSettings.cs` | Outbound auth config | ⏳ Phase 4 |
| `Core/Models/SearchIdentity.cs` | Identity model | ⏳ Phase 5 |
| `Core/Models/CognitiveServicesAccount.cs` | Cognitive services config | ⏳ Phase 5 |
| `Api/Services/Authentication/EntraIdAuthenticationHandler.cs` | Entra ID handler | ⏳ Phase 3 |
| `Api/Controllers/DiagnosticsController.cs` | Diagnostics endpoints | ⏳ Phase 4 |

### Files Modified (Phase 1 & 2)

| File | Changes | Status |
| ---- | ------- | ------ |
| `Api/Program.cs` | Update DI registration, add Phase 2 services | ✅ Modified |
| `Api/appsettings.json` | Add Authentication section, enable Simulated mode | ✅ Modified |
| `Api/AzureAISearchSimulator.Api.csproj` | Add JWT packages | ✅ Modified |
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

- ✅ Phase 1 complete - Foundation infrastructure in place
- ✅ Phase 2 complete - Simulated tokens can be used for all testing without Azure dependency
- Phase 3 (Real Entra ID) requires Azure AD app registration
- Phases 4 & 5 are for outbound/resource-level auth and can be done in parallel
