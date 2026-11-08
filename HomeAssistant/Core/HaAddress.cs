using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;

namespace HomeAssistant;

/// <summary>
/// L'adresse de Home Assistant telle que saisie, normalisée : schéma, hôte et port, avec une barre finale. Dit si elle désigne ce PC
/// (<see cref="IsLocalhost"/>) et si elle est sur le réseau local (<see cref="IsPrivate"/>).
/// </summary>
internal sealed record HaAddress(Uri Root, bool IsLocalhost, bool IsPrivate)
{
    /// <summary>"ha" pour une adresse privée en https (certificat maison, épinglé), sinon null : les adresses publiques ont un certificat signé.</summary>
    public string? CertificateKey => Root.Scheme == Uri.UriSchemeHttps && IsPrivate ? "ha" : null;

    /// <summary>Vrai si l'hôte est une adresse IP (et non un nom, qui demande un DNS).</summary>
    public bool IsIpLiteral => IPAddress.TryParse(Root.Host.Trim('[', ']'), out _);

    /// <summary>
    /// Lit l'adresse saisie. Sans schéma, on suppose <c>http://</c>. Renvoie false si ce n'est pas une adresse http(s) exploitable.
    /// </summary>
    internal static bool TryParse(string? raw, [NotNullWhen(true)] out HaAddress? address)
    {
        address = null;
        var text = (raw ?? "").Trim();
        if (text.Length == 0)
        {
            return false;
        }

        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "http://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || uri.Host.Length == 0)
        {
            return false;
        }

        var root = new Uri($"{uri.Scheme}://{uri.Host}{(uri.IsDefaultPort ? "" : ":" + uri.Port)}/");
        var bare = uri.Host.Trim('[', ']');
        if (IPAddress.TryParse(bare, out var ip))
        {
            address = new HaAddress(root, IPAddress.IsLoopback(ip), IsPrivateAddress(ip));
        }
        else
        {
            address = new HaAddress(root, bare.Equals("localhost", StringComparison.OrdinalIgnoreCase), IsPrivateName(bare));
        }

        return true;
    }

    private static bool IsPrivateAddress(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }

        var bytes = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // fc00::/7 (locale unique) et fe80::/10 (lien local).
            return (bytes[0] & 0xFE) == 0xFC || (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80);
        }

        return bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            || (bytes[0] == 192 && bytes[1] == 168)
            || (bytes[0] == 169 && bytes[1] == 254);
    }

    /// <summary>Un nom sans point (<c>homeassistant</c>), ou en <c>.local</c> ou <c>.lan</c>, se résout sur le réseau local.</summary>
    private static bool IsPrivateName(string host)
    {
        var name = host.TrimEnd('.');
        return !name.Contains('.')
            || name.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".lan", StringComparison.OrdinalIgnoreCase);
    }
}
