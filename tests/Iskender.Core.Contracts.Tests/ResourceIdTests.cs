using System.Globalization;
using System.Security.Cryptography;
using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Tests;

public class ResourceIdTests
{
    private const string Alphabet = "0123456789abcdefghjkmnpqrstvwxyz";
    private const string ValidBody = "0123456789abcdefghjkmnpqrs";

    [Fact]
    public void NewIdHasThePrefixAndACanonicalBody()
    {
        var id = ResourceId.New(ResourceTypes.Endpoint);

        Assert.Same(ResourceTypes.Endpoint, id.Type);
        Assert.Matches("^ep_[0-7][0-9a-hjkmnp-tv-z]{25}$", id.Value);
        Assert.Equal(id.Value, id.ToString());
        Assert.True(ResourceTypes.All.All(type => ResourceId.New(type).Value.Length <= 35));
    }

    [Fact]
    public void ParseRoundTripsAndEqualityIsOrdinalOnTheText()
    {
        var id = ResourceId.New(ResourceTypes.Deployment);
        var parsed = ResourceId.Parse(id.Value, ResourceTypes.Deployment);

        Assert.Equal(id, parsed);
        Assert.Equal(id.GetHashCode(), parsed.GetHashCode());
        Assert.NotEqual(id, ResourceId.New(ResourceTypes.Deployment));
    }

    [Fact]
    public void EqualityOperatorsCompareTheTextNotTheReference()
    {
        var id = ResourceId.New(ResourceTypes.Deployment);
        var parsed = ResourceId.Parse(id.Value, ResourceTypes.Deployment);
        ResourceId? none = null;

        Assert.False(ReferenceEquals(id, parsed));
        Assert.True(id == parsed);
        Assert.False(id != parsed);
        Assert.True(id != ResourceId.New(ResourceTypes.Deployment));
        Assert.False(id == none);
        Assert.True(none == null);
    }

    public static TheoryData<string?, string> Failures() => new()
    {
        { null, "core.resource_id.malformed" },
        { "", "core.resource_id.malformed" },
        { "prj" + ValidBody, "core.resource_id.malformed" },
        { "prj_" + ValidBody + "012345", "core.resource_id.malformed" }, // 36 characters
        { "zzz_" + ValidBody, "core.resource_id.unknown_type" },
        { "_" + ValidBody, "core.resource_id.unknown_type" },
        { "PRJ_" + ValidBody, "core.resource_id.unknown_type" },
        { "req_" + ValidBody, "core.resource_id.unknown_type" },
        { "org_" + ValidBody, "core.resource_id.wrong_type" },
        { "prj_" + ValidBody.ToUpperInvariant(), "core.resource_id.non_canonical" },
        { "prj_8" + ValidBody[1..], "core.resource_id.non_canonical" },
        { "prj_" + ValidBody[..25] + "i", "core.resource_id.non_canonical" },
        { "prj_" + ValidBody[..25] + "u", "core.resource_id.non_canonical" },
        { "prj_" + ValidBody[..25], "core.resource_id.non_canonical" },
        { "prj__" + ValidBody[..25], "core.resource_id.non_canonical" },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public void EachFailureHasItsCodeAndNoPartialValue(string? text, string code)
    {
        Assert.False(ResourceId.TryParse(text, ResourceTypes.Project, out var id, out var error));
        Assert.Null(id);
        Assert.Equal(code, error.Id);

        var exception = Assert.Throws<GatewayException>(() => ResourceId.Parse(text!, ResourceTypes.Project));
        Assert.Equal(code, exception.Code.Id);
    }

    [Fact]
    public void AnErrorMessageHoldsOnlyABoundedSanitizedExcerpt()
    {
        var text = "\u0001\u0002\n\r" + "abcdefghijkl" + "SECRET-TAIL-VALUE";

        var exception = Assert.Throws<GatewayException>(() => ResourceId.Parse(text, ResourceTypes.Project));

        Assert.Equal("core.resource_id.malformed", exception.Code.Id);
        Assert.Contains("????abcdefghijkl", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(exception.Message, char.IsControl);
    }

    [Fact]
    public void AnOversizedInputIsNotEchoed()
    {
        var text = new string('x', 10_000);

        var exception = Assert.Throws<GatewayException>(() => ResourceId.Parse(text, ResourceTypes.Project));

        Assert.Equal("core.resource_id.malformed", exception.Code.Id);
        Assert.DoesNotContain(new string('x', 17), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFailingRandomSourceFailsGenerationWithNoFallback()
    {
        var exception = Assert.Throws<GatewayException>(() =>
            ResourceId.New(ResourceTypes.Project, ResourceTypes.All, _ => throw new CryptographicException("source down")));

        Assert.Equal("core.random_source_failed", exception.Code.Id);
        Assert.IsType<CryptographicException>(exception.InnerException);
    }

    [Fact]
    public void ARetiredTypeCannotBeGeneratedOrParsed()
    {
        var retired = new ResourceType("legacy", "legacy", RegistryStatus.Retired, 1);
        IReadOnlyList<ResourceType> registry = [ResourceTypes.Organization, retired];

        var generate = Assert.Throws<GatewayException>(() => ResourceId.New(retired, registry, RandomNumberGenerator.Fill));
        Assert.Equal("core.resource_id.retired_type", generate.Code.Id);

        Assert.False(ResourceId.TryParse("legacy_" + ValidBody, ResourceTypes.Organization, registry, out var id, out var error));
        Assert.Null(id);
        Assert.Equal("core.resource_id.retired_type", error.Id);

        var parse = Assert.Throws<GatewayException>(() => ResourceId.Parse("legacy_" + ValidBody, retired, registry));
        Assert.Equal("core.resource_id.retired_type", parse.Code.Id);
    }

    [Fact]
    public void AnUnregisteredTypeCannotBeGenerated()
    {
        var stranger = new ResourceType("stranger", "strange", RegistryStatus.Active, 1);

        var exception = Assert.Throws<GatewayException>(() => ResourceId.New(stranger));

        Assert.Equal("core.resource_id.unknown_type", exception.Code.Id);
    }

    // SC-004: 1,000,000 IDs of one type.
    [Fact]
    public void AMillionIdsAreUniqueOpaqueAndUnordered()
    {
        const int sampleSize = 1_000_000;
        var organization = ResourceId.New(ResourceTypes.Organization);
        var project = ResourceId.New(ResourceTypes.Project);
        string[] planted =
        [
            organization.Value, organization.Value[4..], project.Value, project.Value[4..], "operator",
            Environment.MachineName.ToLowerInvariant(),
        ];

        var start = DateTimeOffset.UtcNow;
        var ids = new string[sampleSize];
        for (var i = 0; i < sampleSize; i++)
        {
            ids[i] = ResourceId.New(ResourceTypes.Project).Value;
        }

        var end = DateTimeOffset.UtcNow;

        Assert.Equal(sampleSize, new HashSet<string>(ids, StringComparer.Ordinal).Count);

        var times = new TimeForms(start, end);
        var ascending = 0;
        for (var i = 0; i < sampleSize; i++)
        {
            var body = ids[i].AsSpan(4);
            foreach (var value in planted)
            {
                Assert.False(ids[i].Contains(value, StringComparison.Ordinal), $"{ids[i]} contains a planted value");
            }

            Assert.False(times.AppearsIn(body), $"{ids[i]} contains the generation time");

            if (i > 0 && string.CompareOrdinal(ids[i - 1], ids[i]) < 0)
            {
                ascending++;
            }
        }

        var share = (double)ascending / (sampleSize - 1);
        Assert.InRange(share, 0.49, 0.51);
    }

    [Fact]
    public void EveryIdOfEveryOtherTypeIsRejectedAsWrongType()
    {
        const int perType = 10_000;
        foreach (var other in ResourceTypes.All.Where(type => !ReferenceEquals(type, ResourceTypes.Project)))
        {
            for (var i = 0; i < perType; i++)
            {
                var text = ResourceId.New(other).Value;
                Assert.False(ResourceId.TryParse(text, ResourceTypes.Project, out var id, out var error));
                Assert.Null(id);
                Assert.Same(ErrorCodes.ResourceIdWrongType, error);
            }
        }
    }

    /// <summary>The generation time as <c>yyyyMMdd</c>, and Unix seconds and milliseconds in decimal and base32.</summary>
    private sealed class TimeForms
    {
        private readonly string[] _dates;
        private readonly (long Low, long High, int Length, int Radix)[] _numbers;

        public TimeForms(DateTimeOffset start, DateTimeOffset end)
        {
            _dates = new[] { start, end, start.ToLocalTime(), end.ToLocalTime() }
                .Select(time => time.ToString("yyyyMMdd", CultureInfo.InvariantCulture))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var seconds = (start.ToUnixTimeSeconds(), end.ToUnixTimeSeconds());
            var milliseconds = (start.ToUnixTimeMilliseconds(), end.ToUnixTimeMilliseconds());
            _numbers =
            [
                (seconds.Item1, seconds.Item2, seconds.Item2.ToString(CultureInfo.InvariantCulture).Length, 10),
                (milliseconds.Item1, milliseconds.Item2, milliseconds.Item2.ToString(CultureInfo.InvariantCulture).Length, 10),
                (seconds.Item1, seconds.Item2, Base32Length(seconds.Item2), 32),
                (milliseconds.Item1, milliseconds.Item2, Base32Length(milliseconds.Item2), 32),
            ];
        }

        public bool AppearsIn(ReadOnlySpan<char> body)
        {
            foreach (var date in _dates)
            {
                if (body.Contains(date, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            foreach (var (low, high, length, radix) in _numbers)
            {
                for (var offset = 0; offset + length <= body.Length; offset++)
                {
                    if (TryRead(body.Slice(offset, length), radix, out var value) && value >= low && value <= high)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static int Base32Length(long value)
        {
            var length = 1;
            while ((value >>= 5) > 0)
            {
                length++;
            }

            return length;
        }

        private static bool TryRead(ReadOnlySpan<char> digits, int radix, out long value)
        {
            value = 0;
            foreach (var c in digits)
            {
                var digit = radix == 10 ? (c is >= '0' and <= '9' ? c - '0' : -1) : Alphabet.IndexOf(c, StringComparison.Ordinal);
                if (digit < 0)
                {
                    return false;
                }

                value = (value * radix) + digit;
            }

            return true;
        }
    }
}
