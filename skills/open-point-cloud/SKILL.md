---
name: open-point-cloud
description: Link a PLY or E57 point cloud into a Revit model with the Open Point Cloud add-in, from an agent or a script. Use it when a user asks to link, import or check a PLY or E57 point cloud in Revit.
---

# Open Point Cloud for agents

The Open Point Cloud add-in links a binary PLY or an E57 file as a native Revit point cloud. Agents and scripts use the public API in the namespace `OpenPointCloud`. The API shows no dialogs.

## Requirements

- Revit 2025, 2026 or 2027 with the add-in installed (see README). Revit loads the assembly `OpenPointCloud` at start-up.
- A binary little-endian PLY with `x`, `y` and `z` (float, double or int). Colour (`red`, `green`, `blue`) is optional.
- Or an E57 file. The add-in reads all scans and applies the pose of each scan. Colour and intensity are optional.
- For LAS or LAZ: convert to binary PLY first, for example with CloudCompare.

## API

| Call | Result |
|---|---|
| `Api.Inspect(path)` | `CloudInfo`: file, bytes, points, hasColour, min, max, size (meters, file coordinates), indexFromCache. Needs no document. Makes the index if necessary. |
| `Api.Link(doc, path)` | `LinkResult`: elementId of the point cloud instance, the `CloudInfo` and the time in seconds. |
| `result.ToJson()` | The result as JSON. |

`Api.Link` starts its own transaction. Call it when no transaction is open.

## Procedure

1. Make sure that the active document is the correct model. Do not link test clouds into a production model. Use a copy.
2. Call `Api.Inspect` and report the points, the size and the position to the user.
3. Call `Api.Link`. Keep the `elementId`.
4. Tell the user to align the cloud with Move and Rotate, or align it with the Revit API (`ElementTransformUtils`).
5. Save the model only if the user wants that.

## Examples

pyRevit (no transaction open):

```python
import clr
clr.AddReference("OpenPointCloud")
from OpenPointCloud import Api
print(Api.Link(__revit__.ActiveUIDocument.Document, r"C:\scans\house.ply").ToJson())
```

C# in a Revit MCP server or another add-in (reflection, so no build reference is necessary):

```csharp
var api = Type.GetType("OpenPointCloud.Api, OpenPointCloud");
var result = api.GetMethod("Link").Invoke(null, new object[] { doc, @"C:\scans\house.ply" });
return result.GetType().GetMethod("ToJson").Invoke(result, null);
```

## Facts and traps

- The first link of a file makes a cache next to it: `<name>.ply.opc` or `<name>.e57.opc`. Do not delete it. The next link uses it and takes less than a second.
- The add-in keeps large coordinates (for example national grid coordinates) exact. It puts a 100 m origin in the instance transform.
- Revit tests the view range of a plan against the cloud. If a plan shows no points, compare the Z values of the cloud (`min`, `max`) with the level height.
- An optional sidecar `<name>.ply.align` (or `<name>.e57.align`) with `dx dy dz` in meters moves all points. A change to it makes a new cache.
- Without colour, the add-in uses the E57 intensity as grey. Without colour and intensity, Revit shows the points grey.
