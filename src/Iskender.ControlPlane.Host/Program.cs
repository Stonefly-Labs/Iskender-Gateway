using Iskender.ControlPlane.Host;
using Iskender.Infrastructure.Container;

// The control plane's entry point (container contract, Entry points rule 1). Its registrations, routes, and commands are in
// ControlPlaneDefinition. Owned by ISK-0007.
return await ContainerEntryPoint.RunAsync(args, ControlPlaneDefinition.Create());
