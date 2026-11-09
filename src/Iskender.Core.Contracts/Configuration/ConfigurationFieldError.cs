using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Configuration;

/// <summary>
/// One configuration error: a code, a path, a location, a problem, and a fix. Its text is built from the code, the path,
/// the schema, and the layer only, and never holds a configuration value (FR-020).
/// </summary>
[ContractVersion("core-contracts", 2)]
public sealed class ConfigurationFieldError
{
    public ConfigurationFieldError(ErrorCode code, string path, ConfigurationSourceLocation location, string problem, string fix)
    {
        if (code is null || string.IsNullOrEmpty(path) || location is null || string.IsNullOrEmpty(problem) || string.IsNullOrEmpty(fix))
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        Code = code;
        Path = path;
        Location = location;
        Problem = problem;
        Fix = fix;
    }

    /// <summary>The registered code.</summary>
    public ErrorCode Code { get; }

    /// <summary>
    /// Dot-separated from <c>iskender</c>, with array items as <c>[n]</c>, or <c>(document)</c> or
    /// <c>(environment)</c> for an error not tied to one field. Never empty.
    /// </summary>
    public string Path { get; }

    /// <summary>Where the error is.</summary>
    public ConfigurationSourceLocation Location { get; }

    /// <summary>The broken constraint, such as "must be an integer from 1 to 300".</summary>
    public string Problem { get; }

    /// <summary>How to fix it. Never empty.</summary>
    public string Fix { get; }

    /// <summary>Returns <c>&lt;code&gt; at &lt;path&gt; (&lt;source&gt;:&lt;line&gt;:&lt;column&gt;, &lt;layer&gt;): &lt;problem&gt;. Fix: &lt;fix&gt;</c>.</summary>
    public override string ToString() => $"{Code.Id} at {Path} ({Location}): {Problem}. Fix: {Fix}";
}
