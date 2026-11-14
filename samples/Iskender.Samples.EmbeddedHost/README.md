# Embedded host sample

An ordinary ASP.NET Core application that embeds Iskender in its own process (ISK-0006). It keeps its own service,
middleware, configuration, and `/hello` route beside Iskender, which it composes with `AddIskender(...)` and
`AddIskenderCoordination()` and maps under `/iskender` with `MapIskender(...)`. Iskender's configuration is read from
`iskender.yaml` in the content root.

- How to embed Iskender, and what it does and does not change in a host: the
  [Embedding guide](../../docs/04-technology-stack-and-deployment.md#embedding-guide).
- How to run this sample by hand with `curl`, and how to see a refused start: section 3 of the ISK-0006
  [quickstart](../../specs/0006-host-composition-di-registration/quickstart.md#3-run-the-sample-by-hand).

The hosting tests build this sample in process, on an in-memory server, through `SampleApplication.Create`.
