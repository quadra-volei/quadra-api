using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Quadra.IntegrationTests.Modules.Auth;

/// <summary>
/// Test-only controllers used to exercise the JWT bearer middleware. They are added to the
/// <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/> via
/// <c>AddApplicationPart</c> so production code stays free of test affordances.
/// </summary>
[ApiController]
[Authorize]
[Route("test-protected")]
public sealed class TestProtectedController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            isAuthenticated = User.Identity?.IsAuthenticated ?? false,
            nameIdentifier = User.FindFirstValue(ClaimTypes.NameIdentifier),
            email = User.FindFirstValue("email"),
            role = User.FindFirstValue(ClaimTypes.Role),
        });
    }
}

[ApiController]
[Authorize]
[Route("hubs/test")]
public sealed class TestHubProtectedController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new { isAuthenticated = User.Identity?.IsAuthenticated ?? false });
    }
}

[ApiController]
[Authorize(Roles = "admin")]
[Route("test-admin")]
public sealed class TestAdminProtectedController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok();
}

/// <summary>No [Authorize] and no [AllowAnonymous]: the fallback policy must still demand a user (SEC-15).</summary>
[ApiController]
[Route("test-unattributed")]
public sealed class TestUnattributedController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok();

    /// <summary>An unhandled exception, to check what the client gets back.</summary>
    [HttpGet("boom")]
    public IActionResult Boom() => throw new InvalidOperationException("secret internal detail: connection string xyz");
}
