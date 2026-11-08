# Configuration fixtures

Fixtures for the [configuration contract](../../../../specs/0003-configuration-schema-validation-foundation/contracts/configuration.md). Every file here is copied to the test output and kept LF by `.gitattributes`, so line and column expectations hold on every platform.

| Folder | Holds |
|---|---|
| `valid/` | Documents that validate against the test schemas |
| `invalid/` | Documents that fail, named for the defect, and one folder per error code, `invalid/<code>/` |
| `edge/` | One fixture per edge case in the spec's Edge Cases list |
| `equivalent/<pair>/` | Two inputs, `a.*` and `b.*`, that must give the same snapshot and revision. A `b` split across layers is `b.base.*`, `b.overlay.*`, and `b.env.json` |
| `overlay/` | A base, an overlay, a variable set, and the expected canonical result |

Each negative fixture `<name>.<json|yaml>` has a sidecar `<name>.expect.json` naming every expected error:

```json
{ "errors": [ { "code": "config.field.unknown", "path": "iskender.persistence.conectionTimeout", "line": 4, "column": 5, "layer": "base" } ] }
```

A sidecar may also name the active environment the fixture is validated in, as `"environment": "production"`. Without it, the fixture is validated with no environment, which is production-grade. A sidecar may name environment variables too, as `"variables": { "ISKENDER__PERSISTENCE__HOST": "db" }`, which are applied as the environment layer.

Tests assert on the code, path, line, column, and layer, never on message text.
