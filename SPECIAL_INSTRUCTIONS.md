# Special Instructions

Manual Instructions needed for the deployment of updates in the HOTFIX update for CorporateIdentifiers.

> These steps are required only when upgrading an environment that is already running a
> previous build. For brand-new deployments, follow the standard setup in
> [`README.md`](README.md).

---

### 1. Add new environment variables

Add the following to the **CorporateIdentifierSync function app** configuration. Defaults are
applied in code if a value is missing or invalid, but set them explicitly for clarity.

| Variable | Required | Default | Purpose |
|----------|----------|---------|---------|
| `CorpIDAuditTriggerTime` | Yes (for CorpIDAudit) | — | Cron for the CorpIDAudit function (e.g. `0 0 */6 * * *`). |
| `ReconcileSyncStateTriggerTime` | Yes (for ReconcileSyncState) | — | Cron for the ReconcileSyncState function, which syncs/unsyncs devices when a tag's "enable sync" setting is changed (e.g. `0 0 */12 * * *`). |
| `MAX_CORPIDS_ALLOWED` | No | `10000` | Maximum Corporate Identifiers allowed in the tenant. |
| `MAX_CORPID_RETRIES` | No | `10` | Retries before a device add is marked Failed. |
| `ReconcileSyncBatchSize` | No | `1000` | Devices processed per ReconcileSyncState batch. |
| `CORPID_WARNING_THRESHOLD_PERCENT` | No | `90` | Usage % of `MAX_CORPIDS_ALLOWED` that triggers an audit warning (clamped 1–100). |

### 3. Review/modify current timer triggers

Review the other CorpID timer triggers to make sure they are still appropriate.

| Variable | Required | Default | Purpose |
|----------|----------|---------|---------|
| `AddDevicesTriggerTime` | Yes | — | Cron for the AddDevices function (e.g. `0 */5 * * * *`). |
| `DeleteDevicesTriggerTime` | Yes | — | Cron for the DeleteDevices function (e.g. `0 2-59/5 * * * *`). |
| `ConfirmSyncTriggerTime` | Yes | — | Cron for the ConfirmSync function (e.g. `0 0 */4 * * *`). |

### 3. Turn off syncing at system level

Set the `CorporateIdentifierSync` function app environment variable 
`EnableCorpIDSync` to `false`.  

Confirm the functions are no longer syncing before proceeding.


### 4. Deploy application code

Deploy all software components.

1. Web App (`DelegationStation`)
2. UpdateDevices function app
3. CorporateIdentifierSync function app.

### 5. Run Audit function to get current CorpID count

Run the **CorpIDAudit** function (or otherwise determine the current count of Corporate Identifiers already 
  in use in the tenant).

### 6. Seed the CorpID counter

The CorporateIdentifierSync functions require a `CorpIDCounter` document in CosmosDB. Create
it once using the **SeedCorpIDCounter** triggered webjob. The webjob **refuses to run if a
counter already exists**, so it is safe to re-run.

1. Download the `SeedCorpIDCounter` webjob artifact to a local machine.
2. On the Web App: **Configuration → Application settings**, add `CORPID_INITIAL_COUNT` set to
   that current count (use `0` only if starting completely fresh).
3. On the Web App: **WebJobs → Add** a **Triggered** webjob named `SeedCorpIDCounter`,
   trigger **Manual**, and upload the `seed-corpid-counter-webjob` artifact.
4. **Run** the webjob manually and confirm in the logs that the counter was created.
5. **Remove** the webjob and delete the `CORPID_INITIAL_COUNT` setting afterward.

See [`SeedCorpIDCounter/README.md`](SeedCorpIDCounter/README.md) for full details, including the
document schema that is created.

### 6. Re-enable sync 

Set `EnableCorpIDSync=true` on the CorporateIdentifierSync function app.

---

## Rollback Notes

- Redeploy previous version of code.  
- Manually remove counter from database (to prevent issues on redeployment of changes).
