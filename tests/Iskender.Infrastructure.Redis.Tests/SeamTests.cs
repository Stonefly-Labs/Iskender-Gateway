using System.Reflection;
using System.Runtime.CompilerServices;
using Iskender.Coordination.Conformance;
using Iskender.Infrastructure.Coordination;

namespace Iskender.Infrastructure.Redis.Tests;

public class SeamTests
{
    [Fact]
    public void TheAdapterTheSectionAndTheConformanceLibraryAreReachable()
    {
        var adapter = Assembly.Load(new AssemblyName("Iskender.Infrastructure.Redis"));

        Assert.Equal(["Iskender.Infrastructure.Redis.Tests"], adapter.GetCustomAttributes<InternalsVisibleToAttribute>().Select(grant => grant.AssemblyName));
        Assert.Equal("coordination", CoordinationSectionSchema.Namespace);
        Assert.Equal(CapturingDiagnostics.SourceName, CoordinationTelemetry.SourceName);
    }
}
