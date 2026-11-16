namespace Application.Common;

/// <summary>Messages are shown to clients, so never include internal details.</summary>
public sealed class NotFoundException(string message) : Exception(message);

public sealed class ConflictException(string message) : Exception(message);
