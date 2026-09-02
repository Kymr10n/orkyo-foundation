namespace Orkyo.Foundation.Seed;

/// <summary>
/// Long-option parser for the seed CLIs.
///
/// Replaces CommandLineParser, which has been unmaintained since 2022 and was used for one
/// fixed set of nine flags. Deliberately not reflective: the options are known at compile
/// time, so a dictionary and three typed accessors cover the whole need without attributes,
/// and an unknown flag is rejected rather than silently ignored.
///
/// Grammar: <c>--name value</c>, or a bare <c>--flag</c> for booleans, which may also be
/// written <c>--flag false</c> to switch a default-on flag off.
/// </summary>
public sealed class SeedArgs
{
    private readonly Dictionary<string, string?> _values = new(StringComparer.Ordinal);

    private SeedArgs() { }

    /// <summary>
    /// Parse <paramref name="args"/>, accepting only the names in <paramref name="known"/>.
    /// Returns null and sets <paramref name="error"/> on anything malformed — a typo in a
    /// seed flag should stop the run, not quietly seed the wrong shape.
    /// </summary>
    public static SeedArgs? Parse(string[] args, IReadOnlyCollection<string> known, out string? error)
    {
        var parsed = new SeedArgs();
        error = null;

        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                error = $"Unexpected argument '{token}'. Options take the form --name value.";
                return null;
            }

            var name = token[2..];
            if (name.Length == 0)
            {
                error = "Empty option name '--'.";
                return null;
            }

            // Support --name=value as well as --name value.
            string? value = null;
            var equals = name.IndexOf('=', StringComparison.Ordinal);
            if (equals >= 0)
            {
                value = name[(equals + 1)..];
                name = name[..equals];
            }

            if (!known.Contains(name))
            {
                error = $"Unknown option '--{name}'.";
                return null;
            }

            // A following token that is not itself an option belongs to this option.
            if (value is null && i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = args[++i];
            }

            parsed._values[name] = value;
        }

        return parsed;
    }

    public bool Has(string name) => _values.ContainsKey(name);

    public string? String(string name, string? fallback = null)
        => _values.TryGetValue(name, out var v) && v is not null ? v : fallback;

    public int Int(string name, int fallback)
        => _values.TryGetValue(name, out var v) && int.TryParse(v, out var n) ? n : fallback;

    /// <summary>
    /// Absent takes the default; a bare flag means true; an explicit value is parsed, so a
    /// default-on flag can be switched off with <c>--flag false</c>.
    /// </summary>
    public bool Bool(string name, bool fallback)
    {
        if (!_values.TryGetValue(name, out var v)) return fallback;
        if (v is null) return true;
        return !bool.TryParse(v, out var b) || b;
    }
}
