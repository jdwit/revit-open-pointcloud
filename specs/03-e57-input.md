---
status: done
updated: 2026-11-12
---

# 03 E57 input

## Purpose

Many scanners export E57. The modeller links an E57 file in the same way as a PLY, without a conversion in CloudCompare.

## Function

- The file dialog accepts `.ply` and `.e57`. The API accepts the same files.
- The add-in reads all 3D scans in the E57 file and applies the pose of each scan. It writes one cache `<name>.e57.opc`, as for a PLY. The sidecar `<name>.e57.align` also works.
- Colour and intensity are optional. Without valid colour, the add-in uses the intensity as grey. Without colour and intensity, Revit shows the points grey.
- The status dialog and the API give the same result as for a PLY.

## Reader

The add-in has its own managed E57 reader (`E57.cs`), so the package stays one dll without native libraries.

- File: pages of 1024 bytes. The reader checks the CRC-32C of each page and stops with an error for a damaged or incomplete file.
- Codec: bitpack. This is the only codec in the E57 standard. The reader stops with an error for other codecs.
- Fields: `Float` (single and double), `Integer` and `ScaledInteger`, bit widths 0 to 64.
- Coordinates: Cartesian (`cartesianX/Y/Z`) or spherical (`sphericalRange/Azimuth/Elevation`). The reader skips points with an invalid state.
- Colour and intensity: the reader scales them to 0-255 with the limits of the scan (`colorLimits`, `intensityLimits`) or of the field. It scales an integer range of 0-65535 as `value / 256`.
- `PointFile.Read` selects the PLY or E57 reader from the file extension. Both readers give the same arrays to the index.
- The engine name stays `ply` for all formats, because linked models keep the engine name.

## Results

| Test | Result |
|---|---|
| E57 from CloudCompare (libE57Format 2.1), 30 M points, 452 MB | Read in 1.3 s. 2000 sample points are equal to a libE57Format read (difference 0.0005 mm or less, colour equal) |
| E57 from pye57, 175 k points | Equal to a libE57Format read |
| Two scans with pose, scaled integers, spherical coordinates, invalid points (libE57Format file) | Pass (unit test) |
| 16-bit colour (las2e57 file from libE57Format-test-data) | Pass (unit test) |
| The same cloud as PLY and as E57 | The same index (unit test) |
| Damaged page, incomplete file, other codec, string field, no coordinates | Clear error (unit tests) |
