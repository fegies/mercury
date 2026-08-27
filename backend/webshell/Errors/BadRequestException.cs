namespace backend.Errors
{
    internal class BadRequestException(string? message = null) : WebStatusException(400, message)
    {
    }
}
