---
status: planned
updated: 2026-11-12
---

# 03 E57 input

## Purpose

Many scanners export E57. The modeller links an E57 file in the same way as a PLY, without a conversion in CloudCompare.

## Function

- The file dialog accepts `.e57` and `.ply`.
- The add-in reads all scans in the E57 file, applies the pose of each scan and writes one cache, as for a PLY.
- Colour and intensity are optional. Without colour, the add-in uses the intensity as grey.
- The status dialog and the API give the same result as for a PLY.

## Approach

- The index takes points from a reader interface, so the PLY reader and the E57 reader give the same input.
- The E57 reader must be a .NET library or a small native library in the package. It must not need an installer.

## Open questions

- Which E57 reader: libE57Format (C++, needs a native dll for each platform) or a managed reader?
- Compressed E57 files: does the reader support all compression types that scanners use?

## Tests

- E57 with one scan and with more scans: the points are at the correct position.
- E57 with and without colour.
- The same cloud as PLY and as E57 gives the same index.
