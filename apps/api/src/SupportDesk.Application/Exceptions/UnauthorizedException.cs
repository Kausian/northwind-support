namespace SupportDesk.Application.Exceptions;

/// <summary>
/// Thrown when the caller could not be authenticated, for example a wrong password. Surfaces as
/// HTTP 401 with a deliberately generic message.
/// </summary>
public sealed class UnauthorizedException : Exception
{
    public UnauthorizedException(string message) : base(message)
    {
    }
}
