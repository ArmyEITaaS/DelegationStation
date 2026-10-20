```mermaid
flowchart TD
    START([Start]) --> NSG["Get batch of 'Added' devices in tags<br/>not enabled for syncing"]
    NSG --> LOOP

    subgraph LOOP ["For Each Device"]
        direction TB

        NSS2["Set Status=NotSyncing<br/>LastCorpIdentitySync=Now"]
        NSS2 --> NSU[UpdateDevice]

        NSU -->|Success| X[Done processing device.]
        NSU -->|NotFound| NSNF["Device Deleted from DB.<br/>No action needed."]
        NSU -->|PreconditionFailed| NSPF["DB object modified.<br/>No action needed"]
        NSU -->|Exception| NSEX["DB save will get retried on next run."]

    end

    LOOP --> END[Continue to Syncing Devices]

 ```