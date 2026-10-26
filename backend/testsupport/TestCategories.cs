namespace Orkyo.Foundation.TestSupport;

/// <summary>
/// The xUnit trait on every test class that needs a database (the fixture collections):
/// <c>[Trait(TestCategories.Name, TestCategories.Database)]</c>. <c>check --fast</c> runs
/// everything else with <c>--filter "Category!=Database"</c>. Nothing else expresses "needs a
/// database" to the test runner: a class joins a collection by attribute, and xunit's filter
/// can select on traits but not on collections.
/// </summary>
public static class TestCategories
{
    public const string Name = "Category";
    public const string Database = "Database";
}
