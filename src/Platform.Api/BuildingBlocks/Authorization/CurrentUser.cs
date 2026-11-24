using System.Security.Claims;

namespace Platform.BuildingBlocks.Authorization;

public static class CurrentUser
{
    /// <summary>Reads the user id from the "sub" claim (or the mapped NameIdentifier claim).</summary>
    public static Guid? GetUserId(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
