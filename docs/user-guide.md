# DelegationStation User Guide

## Table of Contents
- [Devices Page](#devices-page)
  - [Viewing Devices](#viewing-devices)
  - [Searching Devices](#searching-devices)
  - [Pagination](#pagination)
  - [Adding a Device](#adding-a-device)
  - [Deleting a Device](#deleting-a-device)
- [Tags Page](#tags-page)

---

## Devices Page

The Devices page (`/`) is the main landing page for DelegationStation.  It lists all devices that belong to tags your account has access to.

### Viewing Devices

The device table displays the following columns for each device:

| Column | Description |
|---|---|
| Make | Device manufacturer (e.g. Dell, HP) |
| Model | Device model name |
| Serial Number | Unique serial number |
| OS | Operating system (Windows, macOS, iOS, Android, etc.) |
| Preferred Hostname | The hostname DelegationStation will attempt to apply |
| Tag | One or more tag labels assigned to the device |
| State | Current sync state (see badge legend below) |

#### Device State Badges

| Badge | Meaning |
|---|---|
| 🔵 **Added** | Device has been added but not yet synced with corporate identifiers |
| 🟢 **Synced** | Device has been successfully synced with corporate identifiers |
| ⚫ **Deleting** | Device is marked for deletion and will be removed on the next sync |
| ⚪ **Added** (white) | Device is in a tag that is not configured to sync corporate identifiers |

### Searching Devices

The row of input fields directly below the column headers acts as a live search filter:

1. Enter text in one or more of the **Make**, **Model**, **Serial Number**, **OS**, or **Preferred Hostname** fields.
2. Click **Search** to retrieve matching devices.
3. The results will reflect only the devices that match all non-empty search criteria.

> **Note:** The search is case-insensitive and performs a partial (contains) match.

### Pagination

The Devices page uses **server-side lazy loading** — only one page of devices is fetched at a time.  This keeps the page fast even when the total number of devices is large.

- The default page size is **10 devices per page**.
- A pagination bar appears at the bottom of the device list when results are returned:

```
« ‹  1 of 5  › »
```

| Control | Action |
|---|---|
| **«** | Jump to the first page |
| **‹** | Go to the previous page |
| **Page indicator** | Shows the current page and total pages (e.g. `1 of 5`) |
| **›** | Go to the next page |
| **»** | Jump to the last page |

> **Tip:** After performing a **Search**, use the pagination controls to browse through the matching results.

### Adding a Device

The **Add device** form is located below the device table.

1. Fill in the required fields: **Device Make**, **Device Model**, and **Serial Number**.
2. (Optional) Set the **Device OS** and **Preferred Hostname**.
3. Use the **Tags** filter input to search and select the tag(s) to assign to the device.
4. Click **Add**.

On success, a green confirmation banner is shown and the new device appears in the table.  
On failure, a red error banner is shown with a correlation ID for troubleshooting.

### Deleting a Device

1. In the device table, click the **🗑 Delete** button on the row of the device you want to remove.
2. A confirmation dialog is shown — review the device details and confirm.
3. The device's state changes to **Deleting**. It will be fully removed from corporate identifiers on the next sync cycle.

---

## Tags Page

See the [Tags documentation](../README.md#dependencies) for information on how tags are configured.  Tags are managed through the **Tags** menu item in the navigation bar and control which groups of users can see and manage which sets of devices.
