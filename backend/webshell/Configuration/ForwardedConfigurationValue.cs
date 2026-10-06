namespace backend.Configuration;

/// <summary>
/// Forwarded headers (reverse proxy) configuration settings
/// </summary>
public class ForwardedConfigurationValue
{
    /// <summary>
    /// IP addresses of additional trusted reverse proxies in front of this
    /// backend (e.g. the TLS terminator ahead of the container's nginx).
    /// Their X-Forwarded-For/Proto/Host headers are honored; the container's
    /// internal proxy (loopback) is always trusted.
    /// </summary>
    public IReadOnlyList<string> TrustedProxies { get; init; } = [];
}
