using System.Security.Cryptography;
using System.Text;
using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;
using Iskender.Domain.Ports;

namespace Iskender.Domain.Tests.Ports;

public class AdvisoryLockKeyTests
{
    private static readonly ResourceId Org = ResourceId.Parse("org_00000000000000000000000001", ResourceTypes.Organization);
    private static readonly ResourceId Prj = ResourceId.Parse("prj_0000000000000000000000000a", ResourceTypes.Project);

    // Computed independently with Python's hashlib and struct.unpack('>q', ...) over the documented byte layout.
    [Fact]
    public void MatchesTheIndependentlyComputedVectorForEachScopeKind()
    {
        Assert.Equal(4424108290327735087, AdvisoryLockKey.Derive(new OrganizationScope(Org), "beta", "log.append"));
        Assert.Equal(-7888242092908515734, AdvisoryLockKey.Derive(new ProjectScope(Org, Prj), "beta", "log.append"));
        Assert.Equal(2838386346886404809, AdvisoryLockKey.Derive(OperatorScope.Instance, "beta", "log.append"));
    }

    [Fact]
    public void DifferentScopesModulesAndNamesGiveDifferentKeys()
    {
        var other = new OrganizationScope(ResourceId.Parse("org_00000000000000000000000002", ResourceTypes.Organization));
        long[] keys =
        [
            AdvisoryLockKey.Derive(new OrganizationScope(Org), "beta", "log.append"),
            AdvisoryLockKey.Derive(other, "beta", "log.append"),
            AdvisoryLockKey.Derive(new OrganizationScope(Org), "alpha", "log.append"),
            AdvisoryLockKey.Derive(new OrganizationScope(Org), "beta", "log.append2"),
            AdvisoryLockKey.Derive(new ProjectScope(Org, Prj), "beta", "log.append"),
            AdvisoryLockKey.Derive(OperatorScope.Instance, "beta", "log.append"),
        ];

        Assert.Equal(keys.Length, keys.Distinct().Count());
    }

    [Fact]
    public void TheMigrationLockKeyIsTheFirstEightBytesOfItsPublishedText()
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes("iskender.migrations.lock.v1"));

        Assert.Equal(6156145740695756181, AdvisoryLockKey.Migration);
        Assert.Equal(0x556F05EF3BB45D95, AdvisoryLockKey.Migration);
        Assert.Equal(AdvisoryLockKey.Migration, System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(hash));
    }

    [Fact]
    public void TheMigrationKeyAppearsInPgLocksAsTheseTwoHalves()
    {
        var key = (ulong)AdvisoryLockKey.Migration;

        Assert.Equal(1433339375u, (uint)(key >> 32));
        Assert.Equal(1001676181u, (uint)(key & 0xFFFFFFFF));
    }

    [Theory]
    [InlineData("Beta", "log")]
    [InlineData("beta", "Log")]
    [InlineData("", "log")]
    [InlineData("beta", "")]
    public void AnInvalidModuleOrNameIsRejected(string module, string name)
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => AdvisoryLockKey.Derive(OperatorScope.Instance, module, name)).Code);
    }

    [Fact]
    public void ANullScopeIsRejected()
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => AdvisoryLockKey.Derive(null!, "beta", "log")).Code);
    }
}
