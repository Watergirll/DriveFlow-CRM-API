using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DriveFlow.Tests.TestInfrastructure;

/// <summary>
/// Information about a protected API endpoint.
/// </summary>
/// <param name="HttpMethod">The HTTP method (GET, POST, PUT, DELETE, etc.).</param>
/// <param name="RoutePattern">The route pattern for the endpoint.</param>
/// <param name="Roles">The roles that are authorized to access this endpoint (empty if any authenticated user).</param>
/// <param name="HasRouteParameters">Whether the route has parameters like {id}.</param>
/// <param name="IsProtected">Whether the endpoint requires authentication.</param>
/// <param name="PolicyNames">The names of authorization policies applied to this endpoint.</param>
public sealed record EndpointInfo(
    string HttpMethod,
    string RoutePattern,
    string[] Roles,
    bool HasRouteParameters = false,
    bool IsProtected = true,
    string[] PolicyNames = null!)
{
    /// <summary>
    /// Gets the policy names, ensuring it's never null.
    /// </summary>
    public string[] PolicyNames { get; init; } = PolicyNames ?? Array.Empty<string>();

    /// <summary>
    /// Returns a string representation suitable for test display.
    /// </summary>
    public override string ToString() => $"{HttpMethod} {RoutePattern}";
}

/// <summary>
/// Provides methods to discover and catalog API endpoints from the application.
/// Extracts routes automatically from the running application without hardcoding.
/// </summary>
public static class ApiRoutesCatalog
{
    /// <summary>
    /// Gets all protected endpoints (those with [Authorize] attribute) from the application.
    /// </summary>
    /// <param name="factory">The WebApplicationFactory to extract endpoints from.</param>
    /// <returns>A list of protected endpoint information.</returns>
    public static List<EndpointInfo> GetProtectedEndpoints<TEntryPoint>(
        WebApplicationFactory<TEntryPoint> factory) where TEntryPoint : class
    {
        var endpoints = new List<EndpointInfo>();

        using var scope = factory.Services.CreateScope();
        var endpointDataSource = scope.ServiceProvider.GetService<EndpointDataSource>();

        if (endpointDataSource == null)
        {
            // Try to get the composite data source
            var dataSources = scope.ServiceProvider.GetServices<EndpointDataSource>();
            foreach (var source in dataSources)
            {
                endpoints.AddRange(ExtractEndpointsFromSource(source, protectedOnly: true));
            }
        }
        else
        {
            endpoints.AddRange(ExtractEndpointsFromSource(endpointDataSource, protectedOnly: true));
        }

        return endpoints;
    }

    /// <summary>
    /// Extracts endpoint information from a single endpoint data source.
    /// </summary>
    private static IEnumerable<EndpointInfo> ExtractEndpointsFromSource(
        EndpointDataSource source, 
        bool protectedOnly = false)
    {
        var endpoints = new List<EndpointInfo>();

        foreach (var endpoint in source.Endpoints)
        {
            // Get the route pattern
            var routeEndpoint = endpoint as RouteEndpoint;
            if (routeEndpoint == null)
                continue;

            var routePattern = routeEndpoint.RoutePattern.RawText ?? string.Empty;

            // Skip Razor Pages and non-API routes
            if (string.IsNullOrEmpty(routePattern) || 
                !routePattern.StartsWith("api/", StringComparison.OrdinalIgnoreCase))
                continue;

            // Get HTTP method(s)
            var httpMethodMetadata = endpoint.Metadata.GetMetadata<HttpMethodMetadata>();
            var methods = httpMethodMetadata?.HttpMethods ?? new[] { "GET" };

            // Check for Authorize attribute
            var authorizeData = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
            var allowAnonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>();
            var isProtected = authorizeData.Any() && allowAnonymous == null;

            if (protectedOnly && !isProtected)
                continue;

            // Extract roles from Authorize attributes
            var roles = authorizeData
                .Where(a => !string.IsNullOrEmpty(a.Roles))
                .SelectMany(a => a.Roles!.Split(',', StringSplitOptions.RemoveEmptyEntries))
                .Select(r => r.Trim())
                .Distinct()
                .ToArray();

            // Extract policy names
            var policyNames = authorizeData
                .Where(a => !string.IsNullOrEmpty(a.Policy))
                .Select(a => a.Policy!)
                .Distinct()
                .ToArray();

            // Check for route parameters
            var hasRouteParameters = routePattern.Contains('{') && routePattern.Contains('}');

            foreach (var method in methods)
            {
                endpoints.Add(new EndpointInfo(
                    method, 
                    routePattern, 
                    roles, 
                    hasRouteParameters, 
                    isProtected, 
                    policyNames));
            }
        }

        return endpoints;
    }

    /// <summary>
    /// Gets all endpoints (both protected and unprotected) from the application.
    /// </summary>
    /// <param name="factory">The WebApplicationFactory to extract endpoints from.</param>
    /// <returns>A list of all endpoint information with authorization status.</returns>
    public static List<(EndpointInfo Endpoint, bool IsProtected)> GetAllEndpoints<TEntryPoint>(
        WebApplicationFactory<TEntryPoint> factory) where TEntryPoint : class
    {
        var endpoints = new List<(EndpointInfo, bool)>();

        using var scope = factory.Services.CreateScope();
        var endpointDataSources = scope.ServiceProvider.GetServices<EndpointDataSource>();

        foreach (var source in endpointDataSources)
        {
            foreach (var endpoint in source.Endpoints)
            {
                var routeEndpoint = endpoint as RouteEndpoint;
                if (routeEndpoint == null)
                    continue;

                var routePattern = routeEndpoint.RoutePattern.RawText ?? string.Empty;

                // Skip non-API routes
                if (string.IsNullOrEmpty(routePattern) || 
                    !routePattern.StartsWith("api/", StringComparison.OrdinalIgnoreCase))
                    continue;

                var httpMethodMetadata = endpoint.Metadata.GetMetadata<HttpMethodMetadata>();
                var methods = httpMethodMetadata?.HttpMethods ?? new[] { "GET" };

                var authorizeData = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
                var allowAnonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>();
                var isProtected = authorizeData.Any() && allowAnonymous == null;

                var roles = isProtected
                    ? authorizeData
                        .Where(a => !string.IsNullOrEmpty(a.Roles))
                        .SelectMany(a => a.Roles!.Split(',', StringSplitOptions.RemoveEmptyEntries))
                        .Select(r => r.Trim())
                        .Distinct()
                        .ToArray()
                    : Array.Empty<string>();

                var policyNames = isProtected
                    ? authorizeData
                        .Where(a => !string.IsNullOrEmpty(a.Policy))
                        .Select(a => a.Policy!)
                        .Distinct()
                        .ToArray()
                    : Array.Empty<string>();

                var hasRouteParameters = routePattern.Contains('{') && routePattern.Contains('}');

                foreach (var method in methods)
                {
                    endpoints.Add((
                        new EndpointInfo(method, routePattern, roles, hasRouteParameters, isProtected, policyNames), 
                        isProtected));
                }
            }
        }

        return endpoints;
    }

    /// <summary>
    /// Gets all API endpoints (protected and unprotected) as EndpointInfo objects.
    /// </summary>
    /// <param name="factory">The WebApplicationFactory to extract endpoints from.</param>
    /// <returns>A list of all API endpoint information.</returns>
    public static List<EndpointInfo> GetAllApiEndpoints<TEntryPoint>(
        WebApplicationFactory<TEntryPoint> factory) where TEntryPoint : class
    {
        return GetAllEndpoints(factory)
            .Select(e => e.Endpoint)
            .ToList();
    }

    /// <summary>
    /// Filters endpoints by HTTP method.
    /// </summary>
    /// <param name="endpoints">The list of endpoints to filter.</param>
    /// <param name="method">The HTTP method to filter by (e.g., "GET", "POST").</param>
    /// <returns>Filtered endpoints matching the specified HTTP method.</returns>
    public static IEnumerable<EndpointInfo> FilterByMethod(
        IEnumerable<EndpointInfo> endpoints,
        string method)
    {
        return endpoints.Where(e =>
            e.HttpMethod.Equals(method, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Filters endpoints by required role.
    /// </summary>
    /// <param name="endpoints">The list of endpoints to filter.</param>
    /// <param name="role">The role to filter by.</param>
    /// <returns>Filtered endpoints that require the specified role.</returns>
    public static IEnumerable<EndpointInfo> FilterByRole(
        IEnumerable<EndpointInfo> endpoints,
        string role)
    {
        return endpoints.Where(e =>
            e.Roles.Any(r => r.Equals(role, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Filters endpoints by policy name.
    /// </summary>
    /// <param name="endpoints">The list of endpoints to filter.</param>
    /// <param name="policyName">The policy name to filter by.</param>
    /// <returns>Filtered endpoints that use the specified policy.</returns>
    public static IEnumerable<EndpointInfo> FilterByPolicy(
        IEnumerable<EndpointInfo> endpoints,
        string policyName)
    {
        return endpoints.Where(e =>
            e.PolicyNames.Any(p => p.Equals(policyName, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Filters endpoints by route pattern prefix.
    /// </summary>
    /// <param name="endpoints">The list of endpoints to filter.</param>
    /// <param name="prefix">The route prefix to filter by (e.g., "api/student").</param>
    /// <returns>Filtered endpoints whose route starts with the specified prefix.</returns>
    public static IEnumerable<EndpointInfo> FilterByRoutePrefix(
        IEnumerable<EndpointInfo> endpoints,
        string prefix)
    {
        return endpoints.Where(e =>
            e.RoutePattern.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Filters to get only endpoints with route parameters.
    /// </summary>
    /// <param name="endpoints">The list of endpoints to filter.</param>
    /// <returns>Filtered endpoints that have route parameters.</returns>
    public static IEnumerable<EndpointInfo> FilterWithRouteParameters(
        IEnumerable<EndpointInfo> endpoints)
    {
        return endpoints.Where(e => e.HasRouteParameters);
    }

    /// <summary>
    /// Filters to get only endpoints without route parameters.
    /// </summary>
    /// <param name="endpoints">The list of endpoints to filter.</param>
    /// <returns>Filtered endpoints that don't have route parameters.</returns>
    public static IEnumerable<EndpointInfo> FilterWithoutRouteParameters(
        IEnumerable<EndpointInfo> endpoints)
    {
        return endpoints.Where(e => !e.HasRouteParameters);
    }

    /// <summary>
    /// Groups endpoints by their controller/route prefix.
    /// </summary>
    /// <param name="endpoints">The list of endpoints to group.</param>
    /// <returns>Endpoints grouped by their route prefix (first two segments).</returns>
    public static IEnumerable<IGrouping<string, EndpointInfo>> GroupByController(
        IEnumerable<EndpointInfo> endpoints)
    {
        return endpoints.GroupBy(e =>
        {
            var segments = e.RoutePattern.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return segments.Length >= 2
                ? $"{segments[0]}/{segments[1]}"
                : e.RoutePattern;
        });
    }

    /// <summary>
    /// Generates a route URL by replacing route parameters with sample values.
    /// </summary>
    /// <param name="endpoint">The endpoint to generate a URL for.</param>
    /// <param name="parameterValues">Dictionary mapping parameter names to values (default: "1" for id parameters).</param>
    /// <returns>A URL string with parameters replaced.</returns>
    public static string GenerateRouteUrl(
        EndpointInfo endpoint, 
        Dictionary<string, string>? parameterValues = null)
    {
        var url = endpoint.RoutePattern;
        
        // Default parameter values
        var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = "1",
            ["schoolId"] = "1",
            ["userId"] = "test-user-id",
            ["fileId"] = "1",
            ["vehicleId"] = "1",
            ["appointmentId"] = "1",
            ["categoryId"] = "1"
        };

        // Merge with provided values
        if (parameterValues != null)
        {
            foreach (var kvp in parameterValues)
            {
                defaults[kvp.Key] = kvp.Value;
            }
        }

        // Replace {param} patterns
        foreach (var kvp in defaults)
        {
            url = url.Replace($"{{{kvp.Key}}}", kvp.Value, StringComparison.OrdinalIgnoreCase);
        }

        // Handle any remaining parameters with a default value
        while (url.Contains('{'))
        {
            var start = url.IndexOf('{');
            var end = url.IndexOf('}', start);
            if (end > start)
            {
                url = url.Remove(start, end - start + 1).Insert(start, "1");
            }
            else
            {
                break;
            }
        }

        return "/" + url.TrimStart('/');
    }

    /// <summary>
    /// Gets a summary of the API structure for documentation/debugging.
    /// </summary>
    /// <param name="factory">The WebApplicationFactory to analyze.</param>
    /// <returns>A formatted string summary of the API structure.</returns>
    public static string GetApiSummary<TEntryPoint>(
        WebApplicationFactory<TEntryPoint> factory) where TEntryPoint : class
    {
        var allEndpoints = GetAllEndpoints(factory);
        var protectedCount = allEndpoints.Count(e => e.IsProtected);
        var unprotectedCount = allEndpoints.Count - protectedCount;

        var byController = GroupByController(allEndpoints.Select(e => e.Endpoint));

        var summary = new System.Text.StringBuilder();
        summary.AppendLine($"API Summary: {allEndpoints.Count} endpoints ({protectedCount} protected, {unprotectedCount} public)");
        summary.AppendLine();

        foreach (var group in byController.OrderBy(g => g.Key))
        {
            summary.AppendLine($"[{group.Key}]");
            foreach (var endpoint in group.OrderBy(e => e.HttpMethod))
            {
                var protection = endpoint.IsProtected ? "??" : "??";
                var roles = endpoint.Roles.Length > 0 ? $" [{string.Join(", ", endpoint.Roles)}]" : "";
                var policies = endpoint.PolicyNames.Length > 0 ? $" (Policy: {string.Join(", ", endpoint.PolicyNames)})" : "";
                summary.AppendLine($"  {protection} {endpoint.HttpMethod,-7} {endpoint.RoutePattern}{roles}{policies}");
            }
            summary.AppendLine();
        }

        return summary.ToString();
    }
}
