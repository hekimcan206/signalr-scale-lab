using System.Globalization;

namespace ScaleLab.LoadGen;

/// <summary><c>--name value</c> argument parsing with environment-variable defaults for the lab addresses.</summary>
internal static class CommandLine
{
    public static Dictionary<string, string> Parse(IEnumerable<string> args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? name = null;
        foreach (var arg in args)
        {
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (name is not null)
                {
                    result[name] = "true";
                }

                name = arg[2..];
            }
            else if (name is not null)
            {
                result[name] = arg;
                name = null;
            }
            else
            {
                throw new ArgumentException($"Unexpected argument '{arg}'.");
            }
        }

        if (name is not null)
        {
            result[name] = "true";
        }

        return result;
    }

    public static string Get(IReadOnlyDictionary<string, string> options, string name, string fallback) =>
        options.TryGetValue(name, out var value) ? value : fallback;

    public static string Required(IReadOnlyDictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) ? value : throw new ArgumentException($"--{name} is required.");

    public static int Int(IReadOnlyDictionary<string, string> options, string name, int fallback) =>
        options.TryGetValue(name, out var value) ? int.Parse(value, CultureInfo.InvariantCulture) : fallback;

    public static LabTarget Target(IReadOnlyDictionary<string, string> options)
    {
        var direct = Get(options, "direct", Env("LAB_DIRECT", "http://hub-1:8080"));
        var proxy = Get(options, "proxy", Env("LAB_PROXY", "http://proxy:8080"));
        var servers = Get(options, "servers", Env("LAB_SERVERS", "http://hub-1:8080,http://hub-2:8080,http://hub-3:8080"))
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => new Uri(s))
            .ToList();
        var key = Get(options, "key", Env("LAB_KEY", string.Empty));
        return new LabTarget(new Uri(direct), new Uri(proxy), servers, key.Length == 0 ? null : key);
    }

    private static string Env(string name, string fallback) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : fallback;
}
