using System.Security.Claims;
using backend.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace backend.Auth;

/// <summary>
/// Binds a <see cref="Guid"/> action parameter from the authenticated user's
/// <c>local_userid</c> claim. Falls back to a forbidden response when the claim
/// is absent or malformed, since every provisioned user carries it.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class FromCurrentUserAttribute : ModelBinderAttribute
{
    public FromCurrentUserAttribute() : base(typeof(CurrentUserIdBinder))
    {
        BindingSource = BindingSource.Custom;
    }
}

public sealed class CurrentUserIdBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var value = bindingContext.HttpContext.User.FindFirstValue("local_userid")
            ?? throw new ForbidException();

        if (!Guid.TryParse(value, out var id))
            throw new ForbidException();

        bindingContext.Result = ModelBindingResult.Success(id);
        return Task.CompletedTask;
    }
}
