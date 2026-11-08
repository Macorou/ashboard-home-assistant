namespace HomeAssistant.Tests;

public sealed class HaAddressTests
{
    [Theory]
    [InlineData("192.168.1.10:8123", "http://192.168.1.10:8123/")]
    [InlineData("http://ha.local:8123/", "http://ha.local:8123/")]
    [InlineData("  https://maison.duckdns.org \n", "https://maison.duckdns.org/")]
    [InlineData("http://u:p@ha.local:8123", "http://ha.local:8123/")]
    [InlineData("https://user:secret@maison.duckdns.org/lovelace?x=1", "https://maison.duckdns.org/")]
    public void TryParse_Normalizes(string raw, string expected)
    {
        Assert.True(HaAddress.TryParse(raw, out var address));
        Assert.Equal(expected, address!.Root.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ftp://x")]
    [InlineData("http://")]
    public void TryParse_Rejects(string raw)
    {
        Assert.False(HaAddress.TryParse(raw, out var address));
        Assert.Null(address);
    }

    [Fact]
    public void TryParse_RejectsMissingAddress()
    {
        Assert.False(HaAddress.TryParse(null, out var address));
        Assert.Null(address);
    }

    [Theory]
    [InlineData("http://10.0.0.5:8123")]
    [InlineData("http://172.16.0.1")]
    [InlineData("http://172.31.255.1")]
    [InlineData("http://192.168.1.10:8123")]
    [InlineData("http://169.254.10.1")]
    [InlineData("http://[fd00::10]:8123")]
    [InlineData("http://ha.local:8123")]
    [InlineData("http://maison.lan")]
    [InlineData("http://homeassistant:8123")]
    public void IsPrivate_IsTrueOnTheLocalNetwork(string raw)
    {
        Assert.True(HaAddress.TryParse(raw, out var address));
        Assert.True(address!.IsPrivate);
    }

    [Theory]
    [InlineData("https://maison.duckdns.org")]
    [InlineData("http://8.8.8.8")]
    [InlineData("http://172.32.0.1")]
    public void IsPrivate_IsFalseOnTheInternet(string raw)
    {
        Assert.True(HaAddress.TryParse(raw, out var address));
        Assert.False(address!.IsPrivate);
    }

    [Theory]
    [InlineData("http://localhost:8123")]
    [InlineData("http://127.0.0.1:8123")]
    [InlineData("http://[::1]:8123")]
    public void IsLocalhost_IsTrueForThisComputer(string raw)
    {
        Assert.True(HaAddress.TryParse(raw, out var address));
        Assert.True(address!.IsLocalhost);
    }

    [Theory]
    [InlineData("http://192.168.1.10:8123")]
    [InlineData("https://maison.duckdns.org")]
    public void IsLocalhost_IsFalseForOtherAddresses(string raw)
    {
        Assert.True(HaAddress.TryParse(raw, out var address));
        Assert.False(address!.IsLocalhost);
    }

    [Fact]
    public void CertificateKey_IsSetForPrivateHttps()
    {
        Assert.True(HaAddress.TryParse("https://192.168.1.10", out var address));
        Assert.Equal("ha", address!.CertificateKey);
    }

    [Fact]
    public void CertificateKey_IsNullForPrivateHttp()
    {
        Assert.True(HaAddress.TryParse("http://192.168.1.10", out var address));
        Assert.Null(address!.CertificateKey);
    }

    [Fact]
    public void CertificateKey_IsNullForPublicHttps()
    {
        Assert.True(HaAddress.TryParse("https://maison.duckdns.org", out var address));
        Assert.Null(address!.CertificateKey);
    }
}
