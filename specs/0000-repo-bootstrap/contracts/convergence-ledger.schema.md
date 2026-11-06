# Contract: Convergence Finding Ledger & Severity Governance

**Applies to**: `.iskender/state/<specId>.converge.json`. Consumer/producer: the `converge` stage
of `/drive-speckit`. Traces: FR-029..FR-033, SC-007.

## JSON shape

```json
{
  "ledgerVersion": 1,
  "specId": "0000",
  "passNumber": 2,
  "findings": [
    {
      "id": "CONV-0000-003",
      "severity": "Low",
      "evidence": "test X asserts Y; see tests/…/File.cs:42",
      "status": "scheduled",
      "firstSeenPass": 1,
      "severityHistory": [],
      "acceptance": null
    }
  ]
}
```

`severity` ∈ {`Low`,`Medium`,`High`,`Critical`}. `status` ∈
{`open`,`scheduled`,`accepted`,`remediated`}. `acceptance` (pass 4+ Low-only) =
`{ owner, rationale, evidence, timestamp }` or `null`. `severityHistory` is append-only, with one
`{ pass, from, to, changedBy, reason, timestamp }` entry per severity change.

## Governance rules

| # | Rule |
|---|---|
| C1 | `converge` **evaluates and records only**; it MUST NOT modify code (no hidden second stage) (FR-030). |
| C2 | Passes 1–3: **all** findings (incl. Low) → `scheduled`; remediation happens only in a separate `implement` invocation (FR-030). |
| C3 | Pass 4+: Low-only findings MAY be `accepted` with recorded `acceptance` fields when no release gate prohibits (FR-031). |
| C4 | Any Medium/High/Critical finding on **any** pass **blocks** and returns to implement/escalation (FR-031). |
| C5 | Zero findings → success permitted immediately, even on pass 1 (FR-032). |
| C6 | Severities MUST NOT be silently downgraded: every change appends to `severityHistory`, and the skill rejects a lower severity without a reason and the Spec Kit maintainer as `changedBy`; `passNumber` and prior findings persist across restarts (FR-032). |
| C7 | After successful convergence, `roadmap-review` becomes eligible; `converge` MUST NOT auto-run it (FR-033). |
