# Test data

| File | Content | Source |
|---|---|---|
| `libe57-two-scans.e57` | Scan 1: grid of 20 x 20 x 20 points, step 0.1 m, scaled integers, colour. Pose: 90 degrees about Z, then (155000, 463000, 2). Scan 2: 100 spherical points (range 1 + n x 0.01, azimuth n x 0.05, elevation 0.1). Intensity n / 99. Every tenth point is invalid. Pose: (10, 0, 0). | Written with libE57Format 3 (pye57) for this project. |
| `las2e57-colour.e57` | 153 points with 16-bit colour | `3rdParty/las2e57/ColourRepresentation.e57` from [libE57Format-test-data](https://github.com/asmaloney/libE57Format-test-data), CC0 1.0 |
