namespace backend.Errors
{
    internal class NotFoundException(string? message = null) : WebStatusException(404, message)
    {
    }
}
