namespace Api.Configuration;

public static class ValidateMode
{
    public static int Run(IConfiguration configuration, string environmentName)
    {
        Console.WriteLine("Orkyo Configuration Validator");
        Console.WriteLine("=============================");
        Console.WriteLine();

        foreach (var key in DeploymentConfig.RequiredKeys)
        {
            var status = !string.IsNullOrEmpty(configuration[key]) ? "✓" : "✗ MISSING";
            Console.WriteLine($"  {key,-45} {status}");
        }

        Console.WriteLine();

        var errors = ConfigurationValidator.Validate(configuration, environmentName);
        if (errors.Count > 0)
        {
            Console.WriteLine("ERRORS:");
            foreach (var error in errors)
                Console.WriteLine($"  ✗ {error}");
            Console.WriteLine();
            Console.WriteLine("Validation FAILED");
            return 1;
        }

        var config = DeploymentConfig.FromConfiguration(configuration);
        Console.WriteLine("Effective configuration (secrets redacted):");
        foreach (var (key, value) in config.Redacted())
            Console.WriteLine($"  {key,-35} = {value ?? "(null)"}");

        Console.WriteLine();
        Console.WriteLine("Validation PASSED");
        return 0;
    }
}
