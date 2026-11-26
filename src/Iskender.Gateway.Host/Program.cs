using Iskender.Gateway.Host;
using Iskender.Infrastructure.Container;

// The data plane's entry point (container contract, Entry points rule 1). Its registrations and routes are in
// DataPlaneDefinition. Owned by ISK-0007.
return await ContainerEntryPoint.RunAsync(args, DataPlaneDefinition.Create());
