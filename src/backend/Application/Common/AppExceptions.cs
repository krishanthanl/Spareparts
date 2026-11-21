namespace Application.Common;

/// <summary>Messages are shown to clients, so never include internal details.</summary>
public sealed class NotFoundException(string message) : Exception(message);

public sealed class ConflictException(string message) : Exception(message);

/// <summary>401: the caller is not (or no longer) signed in. Keep the message generic.</summary>
public sealed class AuthenticationFailedException(string message = AuthenticationFailedException.InvalidCredentials) : Exception(message)
{
    public const string InvalidCredentials = "Email or password is incorrect.";
    public const string SessionEnded = "Your session has ended. Please sign in again.";
}

/// <summary>429: too many attempts in a short time.</summary>
public sealed class RateLimitedException(string message = "Too many attempts. Wait a minute and try again.") : Exception(message);

/// <summary>403: signed in but this action is not allowed for this user.</summary>
public sealed class ForbiddenException(string message) : Exception(message);
