namespace backend.Errors
{
    internal class TooManyRequestsException(string? message = null) : WebStatusException(429, message)
    {
    }
}
