---
status: done
updated: 2026-11-12
---

# 00 Overview

## Purpose

Revit reads point clouds only in the ReCap format (`.rcp`, `.rcs`). Open Point Cloud links point clouds in open formats into Revit, as native Revit point clouds. People use the ribbon button. Agents and scripts use the API.

## Principles

1. Performance. For each view, Revit asks for a box and a point density. The add-in reads the points from a spatial index. It does not read the full file.
2. Simplicity. The add-in is one `.addin` file and one dll. It has no Python and no Autodesk components.
3. Developer experience for the modeller. The cloud works as a usual Revit point cloud: views cut it, the modeller can snap to it, and Move and Rotate align it.

## Design

- Engine: at start-up, the add-in registers a point cloud engine (`IPointCloudEngine`) with the identifier `ply`. The engine reads PLY and E57 files. For each view, Revit asks the engine for points with a filter and a density.
- Index: on the first link, the add-in reads the file and writes a cache (for example `<name>.ply.opc`) next to it. The cache divides the points into cells of 1 m. In each cell, the points have a random order.
- Reading: each read round takes the next 256 points of every cell that the filter accepts. Revit can stop at any point and still has an even sample.
- Position: the cache keeps the points relative to an origin (a multiple of 100 m), so floats keep their precision. The link puts this origin in the instance transform.

## Specs

| Nr | Spec | Status |
|---|---|---|
| 01 | Link a PLY point cloud | done |
| 02 | API for agents and scripts | done |
| 03 | E57 input | done |
| 04 | Distance from the cloud to the model | planned |
