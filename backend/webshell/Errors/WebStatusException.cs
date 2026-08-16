using System;
using Microsoft.AspNetCore.Mvc;

namespace backend.Errors;

/// <summary>
/// A type caught by the exception filter
/// </summary>
public abstract class WebStatusException(int statusCode, string? message) : Exception(message)
{

    /// <summary>
    /// The status code to set on the result
    /// </summary>
    public readonly int StatusCode = statusCode;
}
