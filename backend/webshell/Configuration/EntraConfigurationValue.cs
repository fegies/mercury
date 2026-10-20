namespace backend.Configuration;

/// <summary>
/// Microsoft Entra ID (Azure AD) provider settings
/// </summary>
public class EntraConfigurationValue
{
    /// <summary>
    /// Entra app role values (as defined on the app registration) that grant
    /// the Admin role. Values are read from the 'roles' claim at login, which
    /// Entra populates from the user's app role assignments.
    /// </summary>
    public List<string> AdminAppRoles { get; init; } = [];
}
