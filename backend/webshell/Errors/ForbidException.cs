namespace backend.Errors
{
    internal class ForbidException(string? message = null) : WebStatusException(403, message)
    {
    }
}
