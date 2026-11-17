using Application.Common;

namespace Api.Identity;

public sealed class CurrentActor(IHttpContextAccessor accessor) : ICurrentActor
{
    public string ActorId => accessor.HttpContext is not { } http ? "system"
        : http.User.Identity?.IsAuthenticated == true
            ? http.User.UserId()?.ToString() ?? throw new InvalidOperationException("An authenticated actor must have a user identifier.")
            : "anonymous";
}
