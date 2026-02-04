# MGK Analyzer Current Implementation Overview

## Chart Axis Management
- `MdiChartWindow` keeps a reference to the default chart secondary axis on startup and snapshots its brush/line/tick styles via `AxisAppearanceSnapshot`, enabling deterministic restore whenever the default axis should reappear.
- Custom per-series axes are created with newly allocated `LabelStyle` + `HeaderStyle` objects whose `Foreground` is explicitly the series brush, preventing shared style mutation from Syncfusion themes.
- A `_seriesAxes` dictionary tracks the active custom axes, and `UpdateDefaultValueAxisVisibility()` hides the stock secondary axis while that dictionary is non-empty; when the last axis is removed the cached snapshot repaints the default axis in the original theme.
- Every custom axis now forces `OpposedPosition=false` so every series axis stays on the left rather than alternating sides, keeping the layout static for easier color/axis matching.

## Series Coloring
- Colors originate in `CsvDataLoader.GetDeterministicSeriesColor`, which calculates an FNV-style hash over the trimmed series name and indexes into a fixed palette, guaranteeing stable colors between loads.
- `AddSeriesToChart()` resolves the series brush and calls `ApplySeriesBrush()` before/after adding the `ChartSeries` to ensure Syncfusion styles cannot override `Stroke`, `Interior`, `Fill`, `Brush`, or `Foreground`. Special cases like `FastLineBitmapSeries` and `StepLineSeries` are handled explicitly.
- `DumpChartSeriesBrushes()` now logs the exact brushes applied to each series (`Stroke`, `Interior`, legend-related brushes), giving visibility when verifying color propagation between the data model and the UI.

## Axis Visibility Handling
- `_seriesAxes` stores the `NumericalAxis` objects created per series. While it contains entries, the default `SecondaryAxis` is hidden via a dedicated helper that applies transparent styles; when the dictionary clears, the cached `AxisAppearanceSnapshot` is restored so the default axis reappears exactly as it was before.
- The axis visibility helper now uses the cached snapshot rather than clearing properties, preventing lingering transparency or missing ticks when toggling series.

## CSV Loading Pipeline & Data Handling
- `CsvDataLoader.LoadCsvDataAsync()` opens the CSV with a detected encoding (UTF-8/BOM/EUC-KR fallback) and scans preceding metadata lines (`#KEY:VALUE`) to populate dictionaries for meta type/date, headers, data types, and units. Missing metadata triggers `InvalidDataException` early.
- Once headers/material lines are found, the loader builds `SeriesData` records for each column (excluding time), allocating `float[]` buffers sized by `EstimateRowCountAsync()`. Bit-type columns allocate `BitArray` stores and values remain `float` arrays for efficient SIMD-friendly parsing.
- The streaming loader reads data in 64KB chunks, stitches lines across buffer boundaries, and writes parsed values into the preallocated arrays. The first two rows also seed `dataSet.BaseTime`/`TimeInterval` heuristics.
- After the streaming phase completes, arrays are resized if the actual row count differs from the estimate, and `CalculateStatistics()` runs off the UI thread to fill `SeriesData.Min/Max/Avg` for non-Boolean series using unsafe `float*` traversal for speed.

## Next Steps Guidance
- Maintain the axis/series coloring strategy when integrating new visualization features to keep legend/axis colors synchronized.
- Use the captured default axis snapshot when temporarily hiding the axis during series overlays, avoiding visual glitches.
- The document is meant to align the AI-assisted workflow with current structural patterns before continuing future tasks.