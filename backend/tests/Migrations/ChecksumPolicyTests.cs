using Orkyo.Migrator;

namespace Orkyo.Foundation.Tests.Migrations;

/// <summary>
/// The checksum is what makes applied migrations immutable: the journal stores it on first
/// apply and every later run compares. These pin the two properties the rest of the system
/// relies on — the digest is stable across line-ending drift, and any other byte changes it.
/// </summary>
public sealed class ChecksumPolicyTests
{
    private const string Sql = "CREATE TABLE t (id int);\nINSERT INTO t VALUES (1);\n";

    [Fact]
    public void Compute_IsLowercaseSha256Hex()
    {
        var checksum = ChecksumPolicy.Compute(Sql);

        checksum.Should().HaveLength(64);
        checksum.Should().MatchRegex("^[0-9a-f]{64}$");
        ChecksumPolicy.Algorithm.Should().Be("SHA256");
    }

    [Fact]
    public void Compute_IsDeterministic()
    {
        ChecksumPolicy.Compute(Sql).Should().Be(ChecksumPolicy.Compute(Sql));
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void Compute_IgnoresLineEndingDrift(string lineEnding)
    {
        var drifted = Sql.Replace("\n", lineEnding);

        ChecksumPolicy.Compute(drifted).Should().Be(ChecksumPolicy.Compute(Sql),
            "a checkout on another platform must not invalidate the stored checksum");
    }

    [Theory]
    [InlineData("CREATE TABLE t (id int);\nINSERT INTO t VALUES (2);\n")]   // a value
    [InlineData("CREATE TABLE t (id int);\nINSERT INTO t VALUES (1);")]     // trailing newline
    [InlineData("CREATE TABLE t (id int);\n INSERT INTO t VALUES (1);\n")]  // whitespace
    [InlineData("-- note\nCREATE TABLE t (id int);\nINSERT INTO t VALUES (1);\n")] // a comment
    public void Compute_ChangesForAnyOtherEdit(string edited)
    {
        ChecksumPolicy.Compute(edited).Should().NotBe(ChecksumPolicy.Compute(Sql),
            "only line endings are normalized; every other edit to an applied migration must be detected");
    }

    [Fact]
    public void Compute_RejectsNull()
    {
        var act = () => ChecksumPolicy.Compute(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
