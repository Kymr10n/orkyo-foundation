using Api.Reporting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Orkyo.Foundation.Tests.Services;

public class TenantReportingProvisionerTests
{
    private static TenantReportingProvisioner BuildProvisioner(string masterSecret = "test-secret") =>
        new(
            db: null!,
            engine: null!,
            opts: Options.Create(new ReportingOptions
            {
                Enabled = true,
                ReaderCredentialMasterSecret = masterSecret,
            }),
            logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<TenantReportingProvisioner>.Instance);

    [Fact]
    public void DeriveReaderPassword_IsDeterministic()
    {
        var p = BuildProvisioner();
        var a = p.DeriveReaderPassword("orkyo_acme", 1);
        var b = p.DeriveReaderPassword("orkyo_acme", 1);
        a.Should().Be(b);
    }

    [Fact]
    public void DeriveReaderPassword_DiffersAcrossTenants()
    {
        var p = BuildProvisioner();
        var a = p.DeriveReaderPassword("orkyo_acme", 1);
        var b = p.DeriveReaderPassword("orkyo_beta", 1);
        a.Should().NotBe(b);
    }

    [Fact]
    public void DeriveReaderPassword_DiffersAcrossVersions()
    {
        var p = BuildProvisioner();
        var v1 = p.DeriveReaderPassword("orkyo_acme", 1);
        var v2 = p.DeriveReaderPassword("orkyo_acme", 2);
        v1.Should().NotBe(v2);
    }

    [Fact]
    public void DeriveReaderPassword_DiffersAcrossMasterSecrets()
    {
        var p1 = BuildProvisioner("secret-alpha");
        var p2 = BuildProvisioner("secret-beta");
        p1.DeriveReaderPassword("orkyo_acme", 1)
          .Should().NotBe(p2.DeriveReaderPassword("orkyo_acme", 1));
    }

    [Fact]
    public void DeriveReaderPassword_OutputLength_Is32Chars()
    {
        var p = BuildProvisioner();
        p.DeriveReaderPassword("orkyo_acme", 1).Length.Should().Be(32);
    }

    [Fact]
    public void DeriveReaderPassword_OutputIsUrlSafe()
    {
        var p = BuildProvisioner();
        var pw = p.DeriveReaderPassword("orkyo_acme", 1);
        // Must not contain characters that are problematic in a SQLAlchemy URI or Postgres password.
        pw.Should().MatchRegex(@"^[A-Za-z0-9\-_]+$");
    }
}
