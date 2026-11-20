# Local developer topology (ISK-0008)

This directory runs Iskender on a workstation: PostgreSQL, Valkey, both plane images, built from the ISK-0007
Dockerfiles, and ISK-0009's `worker` service, which runs the control-plane image with its `work` command, with Docker
Compose and no credentials. `config/worker/` and `native/worker/` hold the worker's documents, whose overlay selects
PostgreSQL leases.

| Script | Does |
|---|---|
| `scripts/start.sh` | Builds both plane images and starts the topology, the worker included. `--dependencies` stops the planes and the worker, and starts only PostgreSQL and Valkey |
| `scripts/stop.sh` | Stops the topology and keeps its data |
| `scripts/reset.sh` | Removes the topology's containers, network, volumes, and password file |
| `scripts/migrate.sh` | Runs one migration operation through the control-plane image |
| `scripts/smoke.sh` | Asserts the running topology's expected state, the worker's included |

Prerequisites, options, exit codes, ports, and the expected state before ISK-0021 are in
[Local development](../docs/04-technology-stack-and-deployment.md#local-development). The rules are in the
[local environment contract](../specs/0009-module-events-background-work-coordination/contracts/local-environment.md), version 2.
