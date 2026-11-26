# Local developer topology (ISK-0008)

This directory runs Iskender on a workstation: PostgreSQL, Valkey, both plane images, built from the ISK-0007
Dockerfiles, and ISK-0009's `worker` service, which runs the control-plane image with its `work` command, with Docker
Compose and no credentials. `config/worker/` and `native/worker/` hold the worker's documents, whose overlay selects
PostgreSQL leases. From ISK-0010 the control plane publishes its traffic port on `127.0.0.1`, 18090 by default
(`ISKENDER_LOCAL_CONTROL_PLANE_PORT`), for the admin routes under `/admin/v1`, and `scripts/bootstrap.sh` runs its
`bootstrap` command.

| Script | Does |
|---|---|
| `scripts/start.sh` | Builds both plane images and starts the topology, the worker included. `--dependencies` stops the planes and the worker, and starts only PostgreSQL and Valkey |
| `scripts/stop.sh` | Stops the topology and keeps its data |
| `scripts/reset.sh` | Removes the topology's containers, network, volumes, and password file |
| `scripts/migrate.sh` | Runs one migration operation through the control-plane image |
| `scripts/bootstrap.sh` | Runs `bootstrap issue` or `bootstrap recover` once through the control-plane image, attached; the token is printed to standard output only |
| `scripts/smoke.sh` | Asserts the running topology's expected state, the worker's and the admin routes' included |

Prerequisites, options, exit codes, ports, and the expected state before ISK-0021 are in
[Local development](../docs/04-technology-stack-and-deployment.md#local-development). The rules are in the
[local environment contract](../specs/0010-system-operator-bootstrap-security-baseline/contracts/local-environment.md), version 3,
and the operator steps in [Operator bootstrap](../docs/04-technology-stack-and-deployment.md#operator-bootstrap).
