# Local developer topology (ISK-0008)

This directory runs Iskender on a workstation: PostgreSQL, Valkey, and both plane images, built from the ISK-0007
Dockerfiles, with Docker Compose and no credentials.

| Script | Does |
|---|---|
| `scripts/start.sh` | Builds both plane images and starts the topology. `--dependencies` starts only PostgreSQL and Valkey |
| `scripts/stop.sh` | Stops the topology and keeps its data |
| `scripts/reset.sh` | Removes the topology's containers, network, volumes, and password file |
| `scripts/migrate.sh` | Runs one migration operation through the control-plane image |
| `scripts/smoke.sh` | Asserts the running topology's expected state |

Prerequisites, options, exit codes, ports, and the expected state before ISK-0021 are in
[Local development](../docs/04-technology-stack-and-deployment.md#local-development). The rules are in the
[local environment contract](../specs/0008-local-developer-environment-reproducible-compose/contracts/local-environment.md).
