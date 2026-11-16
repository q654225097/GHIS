using HIS.Server.Host;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace HIS.UnitTests;

public class SecurityKeyConfigurationTests
{
    [Fact]
    public void Resolve_RejectsMissingProductionKeys()
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();
        IHostEnvironment environment = CreateEnvironment("Production");

        var error = Assert.Throws<InvalidOperationException>(
            () => SecurityKeyConfiguration.Resolve(configuration, environment));

        Assert.Contains("生产环境", error.Message);
        Assert.Contains("HIS_JWT_KEY", error.Message);
    }

    [Fact]
    public void Resolve_RejectsWeakKeys()
    {
        IConfiguration configuration = BuildConfiguration(
            jwt: "too-short",
            hmac: new string('h', SecurityKeyConfiguration.MinimumKeyLength),
            aes: new string('a', SecurityKeyConfiguration.MinimumKeyLength));

        var error = Assert.Throws<InvalidOperationException>(
            () => SecurityKeyConfiguration.Resolve(configuration, CreateEnvironment("Production")));

        Assert.Contains("强度不足", error.Message);
    }

    [Fact]
    public void Resolve_ReturnsTrimmedConfiguredKeys()
    {
        string jwt = $" {new string('j', SecurityKeyConfiguration.MinimumKeyLength)} ";
        string hmac = $" {new string('h', SecurityKeyConfiguration.MinimumKeyLength)} ";
        string aes = $" {new string('a', SecurityKeyConfiguration.MinimumKeyLength)} ";
        IConfiguration configuration = BuildConfiguration(jwt, hmac, aes);

        SecurityKeys keys = SecurityKeyConfiguration.Resolve(
            configuration,
            CreateEnvironment("Development"));

        Assert.Equal(jwt.Trim(), keys.JwtSigningKey);
        Assert.Equal(hmac.Trim(), keys.HmacKey);
        Assert.Equal(aes.Trim(), keys.AesMasterKey);
    }

    private static IConfiguration BuildConfiguration(string jwt, string hmac, string aes)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HIS_JWT_KEY"] = jwt,
                ["HIS_HMAC_KEY"] = hmac,
                ["HIS_AES_KEY"] = aes
            })
            .Build();
    }

    private static IHostEnvironment CreateEnvironment(string name)
    {
        return new TestHostEnvironment(name);
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "HIS.UnitTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
