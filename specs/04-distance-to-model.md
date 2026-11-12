---
status: planned
updated: 2026-11-12
---

# 04 Distance from the cloud to the model

## Purpose

The modeller sees where the model is different from the scan. An agent gets the same data as numbers.

## Function

- The modeller selects elements, for example walls and floors, and starts the check.
- The add-in calculates, for each point near the selected elements, the distance to the nearest face.
- The cloud shows the distance as colour: green within a tolerance, then a colour scale up to a maximum. The modeller sets the tolerance (default 20 mm).
- The add-in gives, for each element, the number of points, the mean distance and the largest distance.
- The API gives the same result as JSON.

## Approach

- The engine already sets the colour for each point. A second colour mode uses the distance instead of the colour of the PLY.
- The add-in gets the faces of the elements with the Revit API. It calculates the distances before Revit reads the points, so views stay fast.

## Open questions

- Only points near the selected elements, or all points with a maximum distance?
- Does the add-in write the result to a parameter of each element?

## Tests

- A wall that is 50 mm out of position shows red points and a mean distance of approximately 50 mm.
- A wall at the correct position shows green points.
- The JSON result has the same values as the colour.
