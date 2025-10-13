# Syncfusion WPF usage instructions for MGK_Analyzer

This file provides guidance for AI assistants and contributors on how Syncfusion WPF components are used in this repository.

## Packages used
- Syncfusion.SfChart.WPF v31.1.17
- Syncfusion.SfGrid.WPF v31.1.17
- Syncfusion.Themes.FluentLight.WPF v31.1.17
- Syncfusion.Themes.MaterialLight.WPF v31.1.17
- Syncfusion.Themes.MaterialDark.WPF v31.1.17

## Key integration points
- `App.xaml.cs` registers Syncfusion license using `SyncfusionLicenseProvider.RegisterLicense(...)`.
- Charts are hosted inside `MainWindow.xaml` `Canvas` with x:Name `MdiCanvas`.
- Theme selection uses `ThemeManager` service and Syncfusion theme packages.

## Recommended usage patterns
1. Register license once during application startup (App.OnStartup) and avoid hardcoding license in source for production.
2. Instantiate Syncfusion UI controls on the UI thread; bind data via ViewModel and update UI through data binding.
3. For large datasets, apply downsampling, virtualization and avoid retaining copies of large arrays in UI controls.
4. Use Syncfusion's `SfSkinManager` to apply themes to controls consistently.

## Typical requests to ask AI
- "Show me an example of creating an `SfChart` time series in XAML and binding a `MemoryOptimizedDataSet` to it." 
- "Suggest a sampling strategy for rendering 1M points efficiently with `SfChart` and update the `CsvDataLoader` accordingly." 
- "How to apply a Syncfusion theme dynamically to an existing window at runtime? Provide code example."

## Useful links
- Syncfusion WPF documentation: https://www.syncfusion.com/documentation/wpf
- Syncfusion API reference: https://help.syncfusion.com/

```
Note: This file is used by Copilot Chat if repo-instructions are enabled in Copilot settings.
```