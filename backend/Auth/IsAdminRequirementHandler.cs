using System;
using Microsoft.AspNetCore.Authorization;

namespace backend.Auth;

class IsAdminRequirementHandler : AuthorizationHandler<IsAdminRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, IsAdminRequirement requirement)
    {
        var is_admin = context.User.FindAll("mercury.role").Any(c => c.Value == "Admin");

        if (is_admin)
            context.Succeed(requirement);
        else
            context.Fail(new AuthorizationFailureReason(this, "You are not an admin in this application."));

        return Task.CompletedTask;
    }
}
