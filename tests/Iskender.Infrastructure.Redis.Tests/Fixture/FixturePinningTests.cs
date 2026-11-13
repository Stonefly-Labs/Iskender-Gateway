using System.Text.RegularExpressions;

namespace Iskender.Infrastructure.Redis.Tests.Fixture;

public partial class FixturePinningTests
{
    [Fact]
    public void TheImageIsPinnedByTheDigestRecordedInT002()
    {
        var match = ReferencePattern().Match(RedisFixture.Image);

        Assert.True(match.Success, "The image reference must end in @sha256: and 64 lowercase hex characters.");
        Assert.Equal("valkey/valkey", match.Groups["repository"].Value);
        Assert.Equal("8.1.11", match.Groups["tag"].Value);
        Assert.Equal("sha256:8f836008e0986c36adf50da714c7060cf689dbb335f61fe077939d90fa650809", match.Groups["digest"].Value);
    }

    [GeneratedRegex(@"\A(?<repository>[a-z0-9./-]+):(?<tag>[A-Za-z0-9._-]+)@(?<digest>sha256:[0-9a-f]{64})\z", RegexOptions.CultureInvariant)]
    private static partial Regex ReferencePattern();
}
