# Limitations & Future Improvements

## Current Limitations

### Nullable Reference Warnings
**Issue**: 80 nullable reference warnings across both projects
**Impact**: Non-blocking build warnings
**Locations**: 
- MVC Services: Null reference return warnings
- API Controllers: Potential null reference assignments
- ViewModels: Non-nullable property initialization

**Evidence**:
```
warning CS8603: Possible null reference return.
warning CS8629: Nullable value type may be null.
warning CS8618: Non-nullable property must contain a non-null value.
```

### API Has No Authentication
**Issue**: Login returns the user, but API endpoints do not require a token. Role checks happen in the MVC layer only.
**Impact**: Anyone who can reach the API can call it directly. Fine for a local school project, not for production.
**Next step**: JWT or cookie auth with `[Authorize(Roles = ...)]` on the API controllers.

### Profile Image Fallback System
**Issue**: Multiple fallback mechanisms for profile images indicate potential complexity
**Location**: Multiple views with onerror handlers and avatar generation
**Impact**: Increased client-side complexity for image handling

### Cancelling and Refunds
**Issue**: Only a *Pending* order is refunded when cancelled. Cancelling a *Ready* order marks it cancelled without returning the money (a test documents this).
**Next step**: Decide the refund rule for Ready orders and cover it with a test.

### Certificate Handling in the MVC Client
**Issue**: `AuthService.Login` uses a plain `HttpClient`, so it fails against an untrusted development certificate, while `Register` and `GetUserById` disable certificate validation. The two behave differently, and the second is only acceptable for local development.
**Next step**: One shared `HttpClient` from `IHttpClientFactory`, with the development certificate trusted instead of bypassed.

### Routing and Small UI Gaps
**Issue**: Controller URLs need the action name (`/Products/Index`, not `/Products`) because the default route only has a default for the Auth controller. After a successful checkout the cart table stays on screen until the page reloads.

### Session-Based Authentication
**Issue**: Authentication relies on server-side sessions
**Impact**: Limited scalability and no support for distributed deployment
**Location**: All MVC controllers use HttpContext.Session

## Technical Debt

### ErrorViewModel Reference
**Issue**: Single domain model reference in MVC project violates strict separation
**Location**: `refeitorio/Models/ErrorViewModel.cs` and related references
**Impact**: Architectural inconsistency, but required by ASP.NET Core framework

### API Base URL Default Duplicated
**Issue**: The API URL is read from `ApiSettings:BaseUrl`, but each service repeats the `https://localhost:7091/api` fallback instead of sharing one typed options class.
**Impact**: Easy to miss when changing environments.

### Duplicate Color Generation Logic
**Issue**: Avatar color generation logic duplicated across views
**Location**: Profile/Index.cshtml and _Layout.cshtml
**Impact**: Code maintenance overhead

## Areas Requiring Future Attention

### Database Migration Strategy
**Current State**: Manual SQL script execution
**Future Need**: Automated migration system for deployment
**Evidence**: `RefeitorioDB.sql` file requires manual execution

### API Documentation
**Current State**: Swagger UI available but not customized
**Future Need**: Comprehensive API documentation for external consumers
**Evidence**: Basic Swashbuckle configuration with default setup

### Error Handling Consistency
**Current State**: Mixed error handling approaches across services
**Future Need**: Standardized error handling and logging strategy
**Evidence**: Different error handling patterns in various service classes

### Testing Coverage
**Current State**: No automated tests visible in project structure
**Future Need**: Unit and integration test implementation
**Evidence**: No test projects or test files in solution structure

## Performance Considerations

### HTTP Call Overhead
**Issue**: Every data operation requires HTTP round-trip to API
**Impact**: Potential latency in user interactions
**Mitigation**: Current implementation acceptable for single-server deployment

### Static Asset Management
**Issue**: Large number of product images stored in wwwroot
**Impact**: Increased deployment size and memory usage
**Evidence**: 50+ product images in wwwroot/images folder

## Deployment Readiness

### Environment-Specific Configuration
**Issue**: Configuration files contain development-specific values
**Future Need**: Environment-based configuration management
**Evidence**: appsettings.json files with development connection strings

### HTTPS Certificate Management
**Issue**: Development certificates may not work in production
**Future Need**: Proper certificate configuration for production deployment
**Evidence**: TrustServerCertificate=true in connection strings
