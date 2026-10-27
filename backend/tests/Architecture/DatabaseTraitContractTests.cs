using System.Reflection;

namespace Orkyo.Foundation.Tests.Architecture;

/// <summary>
/// The ratchet behind <c>check --fast</c>: a class that joins a database collection must carry
/// <see cref="TestCategories.Database"/>, or the fast filter would run it without a server; and a
/// class carrying the trait must be in a collection, or a database-free test would be skipped
/// before every push for no reason.
/// </summary>
public class DatabaseTraitContractTests
{
    private static IEnumerable<Type> TestClasses => typeof(DatabaseTraitContractTests).Assembly.GetTypes()
        .Where(t => t.IsClass && !t.IsAbstract
                    && t.GetMethods().Any(m => m.GetCustomAttributes<FactAttribute>(inherit: true).Any()));

    private static bool InCollection(Type t) => t.GetCustomAttribute<CollectionAttribute>() is not null;

    // TraitAttribute keeps its name and value private; the attribute data still carries them.
    private static bool HasDatabaseTrait(Type t) => t.GetCustomAttributesData()
        .Where(a => a.AttributeType == typeof(TraitAttribute))
        .Any(a => a.ConstructorArguments.Count == 2
                  && Equals(a.ConstructorArguments[0].Value, TestCategories.Name)
                  && Equals(a.ConstructorArguments[1].Value, TestCategories.Database));

    [Fact]
    public void EveryCollectionClass_CarriesTheDatabaseTrait()
    {
        var missing = TestClasses.Where(t => InCollection(t) && !HasDatabaseTrait(t))
            .Select(t => t.FullName).OrderBy(n => n).ToList();

        missing.Should().BeEmpty(
            "these classes use a database collection but lack [Trait(TestCategories.Name, TestCategories.Database)], "
            + "so check --fast would run them without a server:\n  " + string.Join("\n  ", missing));
    }

    [Fact]
    public void OnlyCollectionClasses_CarryTheDatabaseTrait()
    {
        var stray = TestClasses.Where(t => HasDatabaseTrait(t) && !InCollection(t))
            .Select(t => t.FullName).OrderBy(n => n).ToList();

        stray.Should().BeEmpty(
            "these classes carry the Database trait but use no collection, so check --fast skips database-free "
            + "tests:\n  " + string.Join("\n  ", stray));
    }
}
