namespace backend.Configuration;

/// <summary>
/// Microsoft Entra ID (Azure AD) provider settings
/// </summary>
public class EntraConfigurationValue
{
    /// <summary>
    /// Entra group object ids whose members are granted the Admin role.
    /// Membership is read from the 'groups' claim at login.
    /// </summary>
    public List<string> AdminGroupIds { get; init; } = [];
}
