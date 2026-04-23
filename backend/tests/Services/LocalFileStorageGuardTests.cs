using Api.Services;
using FluentAssertions;
using Xunit;

namespace Orkyo.Foundation.Tests.Services;

public class LocalFileStorageGuardTests
{
    private static string BasePath => Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "orkyo-fsguard-tests"));

    [Fact]
    public void AssertWithinBasePath_AcceptsPathInsideBase()
    {
        var inside = Path.Combine(BasePath, "tenant_x", "floorplans", "img.png");

        var act = () => LocalFileStorageGuard.AssertWithinBasePath(BasePath, inside);

        act.Should().NotThrow();
    }

    [Fact]
    public void AssertWithinBasePath_AcceptsBasePathItself()
    {
        var act = () => LocalFileStorageGuard.AssertWithinBasePath(BasePath, BasePath);

        act.Should().NotThrow();
    }

    [Fact]
    public void AssertWithinBasePath_RejectsParentDirectoryEscape()
    {
        // ../etc/passwd-style traversal should resolve outside BasePath
        var escape = Path.Combine(BasePath, "..", "etc", "passwd");

        var act = () => LocalFileStorageGuard.AssertWithinBasePath(BasePath, escape);

        act.Should().Throw<ArgumentException>().WithMessage("Invalid file path.");
    }

    [Fact]
    public void AssertWithinBasePath_RejectsAbsolutePathOutsideBase()
    {
        var outside = Path.Combine(Path.GetTempPath(), "orkyo-fsguard-tests-other", "x.png");

        var act = () => LocalFileStorageGuard.AssertWithinBasePath(BasePath, outside);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AssertWithinBasePath_RejectsSiblingPrefixCollision()
    {
        // Defends against the classic "/var/data" vs "/var/data-evil" bug where
        // a naive StartsWith without DirectorySeparatorChar would accept the
        // sibling. We use BasePath + suffix (no separator) to construct it.
        var siblingPrefix = BasePath + "-evil";

        var act = () => LocalFileStorageGuard.AssertWithinBasePath(BasePath, siblingPrefix);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void IsWithinBasePath_NonThrowingMirrorsAssertOutcome()
    {
        var inside = Path.Combine(BasePath, "ok.png");
        var outside = Path.Combine(Path.GetTempPath(), "orkyo-fsguard-tests-other", "x.png");

        LocalFileStorageGuard.IsWithinBasePath(BasePath, inside).Should().BeTrue();
        LocalFileStorageGuard.IsWithinBasePath(BasePath, outside).Should().BeFalse();
    }
}
