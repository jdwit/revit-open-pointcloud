---
status: done
updated: 2026-11-12
---

# 02 API for agents and scripts

## Purpose

An agent or a script links a cloud without clicks and without dialogs, and gets the result as data.

## Calls

The path is a PLY or E57 file.

| Call | Result |
|---|---|
| `Api.Inspect(path)` | `CloudInfo`: file, bytes, points, hasColour, min, max, size (meters, file coordinates), indexFromCache. Needs no document. Makes the index if necessary. |
| `Api.Link(doc, path)` | `LinkResult`: elementId of the point cloud instance, the `CloudInfo` and the time in seconds. Starts its own transaction. |
| `LinkResult.ToJson()` | The result as JSON with camelCase names. |

The namespace is `OpenPointCloud`. The assembly is `OpenPointCloud`.

## Agent instructions

`skills/open-point-cloud/SKILL.md` is the only source of agent instructions: when to use the add-in, the procedure, examples and traps. The README refers to it.

## Results

| Test | Result |
|---|---|
| `Api.Link` through a Revit MCP server (C#, reflection) | Pass. 2.9 M points from the cache in 0.06 s, JSON result |
| `Api.Inspect` through pyRevit (IronPython, `clr.AddReference`) | Pass |
| Unit tests for `Inspect`, colour detection and JSON | Pass |
