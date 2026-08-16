using System;
using System.Security.Claims;
using appcore.Entities;

namespace backend.Services;

interface IUserProvisioner
{
    /// <summary>
    /// Finish user provisioning by
    /// - setting the profile url
    /// - mapping roles from oidc
    /// </summary>
    /// <param name="user"></param>
    /// <param name="principal"></param>
    /// <returns></returns>
    Task ProvisionUser(UserEntity user, ClaimsPrincipal principal);
}
