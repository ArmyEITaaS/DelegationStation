# SeedCorpIDCounter Webjob

## Purpose

This is a **one-time** webjob that creates the initial `CorpIDCounter` document in CosmosDB. The `CorporateIdentifierSync` function app requires this document to track corporate identifier usage. 

**Safety:** This webjob will **refuse to run** if a `CorpIDCounter` document already exists in the database. It will not overwrite or modify an existing counter.

Instructions are limited to use of this webjob and will be a part of larger deployment instructions.

## Required Environment Variables

| Variable | Required | Description |
|----------|----------|-------------|
| `CORPID_INITIAL_COUNT` | **Yes** | The initial value to set for `CorpIDCount`. This should reflect the current number of corporate identifiers already in use in your tenant. Set to `0` if starting fresh. |

\* The webjob will also leverage existing environment variables and permissions of the DelegationStation webapp which are not listed. 

## Instructions

1. **Build/Publish** the project:
   - The automated build will generate a zip file (e.g., `SeedCorpIDCounter.zip`).

2. Run the CorpIDAudit job to get the current count of CorporateIdentifiers in the system.

3. **Set environment variables** in the Azure App Service (or WebJob host):
   - Navigate to the App Service → **Configuration** → **Application settings**
   - Add `CORPID_INITIAL_COUNT` with the desired initial count value

4. **Upload** the zip as a triggered webjob:
   - Navigate to the App Service → **WebJobs**
   - Click **Add**
   - Name: `SeedCorpIDCounter`
   - Type: **Triggered**
   - Upload the zip file
   - Triggers: **Manual**

5. **Run** the webjob manually from the Azure Portal.

6. **Verify** in the webjob logs that the `CorpIDCounter` was created successfully.

7. **Remove** the webjob from the webapp deployment and the `CORPID_INITIAL_COUNT` environment variable after successful execution.

## What It Creates

A single document in the CosmosDB container with the following structure:

```json
{
	"id": "<new-guid>",
	"PartitionKey": "CorpIDCounter",
	"CorpIDCount": <value-from-CORPID_INITIAL_COUNT>,
	"CorpIDReserve": 0,
	"CreatedDT": "<utc-timestamp>",
	"ModifiedDT": "<utc-timestamp>"
}
```

