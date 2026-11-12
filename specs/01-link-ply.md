---
status: done
updated: 2026-11-12
---

# 01 Link a PLY point cloud

## Function

1. The modeller clicks Add-Ins > Open Point Cloud > Link point cloud and selects a `.ply` file.
2. The PLY can be in Downloads, on the desktop or in a temporary folder. Then the add-in offers to copy it to the folder of the Revit model. A file in these folders often moves or goes away, and then the link breaks.
3. If no current cache exists, the add-in makes the index and shows a progress bar.
4. The add-in links the cloud in one transaction, selects it and zooms to it.
5. A status dialog shows the file and its size, the number of points, and the size and position of the cloud. It also shows the index source (cache or new) and the time. It shows "no colour" only if the PLY has no colour.

## Input

- PLY, binary little-endian. Other PLY formats give a clear error.
- `x`, `y` and `z` as float, double or int. Colour (`red`, `green`, `blue`) is optional. Without colour, Revit shows the points grey.
- The add-in ignores other vertex properties.

## Index and cache

- The cache `<name>.ply.opc` is next to the PLY. The header holds the size and time of the PLY. If the PLY changes, the add-in makes a new index.
- An optional sidecar `<name>.ply.align` holds a move `dx dy dz` in meters. A change to the sidecar makes a new index.
- Without the PLY, the cache is enough to show the cloud.

## Large coordinates

The cache keeps the points relative to an origin (a multiple of 100 m). The link puts the origin in the instance transform, because Revit ignores `IPointCloudAccess.GetOffset()`. A scan in national grid coordinates keeps sub-millimetre precision.

## Ribbon button

The button uses the icon of the Revit Point Cloud button (Insert tab). The add-in reads this icon at start-up, so the repo has no Autodesk images. If Revit has no such button, the add-in uses its own icon.

## Revit versions

Revit 2025 and 2026 (.NET 8) and Revit 2027 (.NET 10). The build gets the Revit API reference assemblies from NuGet.

## Results

| Test | Result |
|---|---|
| Index of 3.5 M points | 0.8 s |
| Index of 31 M points | 3.7 s. Link 0.1 s. 3D view 0.5 s. The build uses approximately 2 GB of memory |
| Link from the cache | Less than 0.1 s |
| Plan, section, section box and crop | They cut the cloud |
| Snap to points | Two points 500.000 mm apart give a line of 500.000 mm |
| Large coordinates | After a move back, the cloud matches the original within 0.00001 mm |
| Move and Rotate | Views and snap stay correct |
| Save, close and open | The cloud loads from the cache |

Unit tests (`tests/`) cover the PLY reader, the index, the cache, the sidecar, large coordinates and the read rounds. The parts that use Revit types (engine registration, button, dialogs) have manual tests in Revit 2027.

## Notes

- Revit tests the view range of a plan against the cloud extent. If a plan shows no points, compare the Z values of the cloud with the level height.
- `GetExtent()` is in cloud units, not in feet.
- For snapping, Revit calls `ReadPoints` with a filter of a few centimetres around the cursor.
- To unlink, delete the instance. Revit then deletes the type.
