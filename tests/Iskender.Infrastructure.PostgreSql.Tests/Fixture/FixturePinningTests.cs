using System.Text.RegularExpressions;

namespace Iskender.Infrastructure.PostgreSql.Tests.Fixture;

/// <summary>
/// The fixture's image is pinned by digest, never by a moving tag alone (User Story 5 scenario 2, FR-033, research R19).
/// Pulling it is the suite's only network access (scenario 3, SC-001).
/// </summary>
public partial class FixturePinningTests
{
    [Fact]
    public void TheImageIsPinnedByASha256Digest()
    {
        var match = PinnedImage().Match(PostgreSqlFixture.Image);

        Assert.True(match.Success, "The image reference must end in @sha256: and 64 lowercase hex characters.");
        Assert.Equal("postgres", match.Groups["repository"].Value);
        Assert.StartsWith("17", match.Groups["tag"].Value, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"\A(?<repository>[a-z0-9./-]+)(:(?<tag>[A-Za-z0-9._-]+))?@sha256:[0-9a-f]{64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex PinnedImage();
}
