Syncfusion WPF Component Overview for MGK_Analyzer

Purpose
- Provide local, human- and AI-readable summary of key Syncfusion WPF components used in this repository.
- Offer quick usage snippets and links to official docs so Copilot/Copilot Chat and contributors can answer API questions accurately.

Packages in this repo
- Syncfusion.SfChart.WPF v31.1.17
- Syncfusion.SfGrid.WPF v31.1.17
- Syncfusion.Themes.FluentLight.WPF v31.1.17
- Syncfusion.Themes.MaterialLight.WPF v31.1.17
- Syncfusion.Themes.MaterialDark.WPF v31.1.17

Key components and common properties/methods

1) SfChart (Syncfusion.UI.Xaml.Charts)
- Common series: LineSeries, ColumnSeries, ScatterSeries
- Key properties on SfChart:
  - PrimaryAxis / SecondaryAxis: set axis types (DateTimeAxis, NumericalAxis, CategoryAxis)
  - Series: collection of series to render
  - Legend: show/hide legend
- LineSeries common props:
  - ItemsSource: IEnumerable data source
  - XBindingPath / YBindingPath: property names for X/Y
  - Stroke, StrokeThickness, Fill
- Performance tips:
  - Use downsampling on very large datasets before binding
  - Avoid binding millions of points directly; instead provide sampled data or streaming visualization

2) SfDataGrid (Syncfusion.UI.Xaml.Grid)
- Key features: virtualization, sorting, grouping, fast rendering for large datasets
- Important props:
  - ItemsSource: collection to display
  - AutoGenerateColumns: bool
  - EnableVirtualization: true for large sets
  - ColumnSizer, QueryCellStyle for custom rendering

3) SfSkinManager / Themes
- Use SfSkinManager to apply Syncfusion themes at runtime
- Example: `SfSkinManager.ApplyStylesOnElement(window, new Theme() { ThemeName = "MaterialLight" });` (refer to Syncfusion docs for exact API for used version)
- Alternatively, include theme assemblies and set styles in App.xaml

4) Licensing
- Register once at startup with `SyncfusionLicenseProvider.RegisterLicense(licenseKey)`
- Keep license keys out of source code in production; use environment variables / user secrets

Examples (short)

- Register license (App.OnStartup)
```csharp
Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(Environment.GetEnvironmentVariable("SYNCFUSION_LICENSE"));
```

- Basic SfChart XAML
```xaml
<syncfusion:SfChart>
  <syncfusion:SfChart.PrimaryAxis>
    <syncfusion:DateTimeAxis IntervalType="Days" />
  </syncfusion:SfChart.PrimaryAxis>
  <syncfusion:LineSeries ItemsSource="{Binding SeriesData}" XBindingPath="Time" YBindingPath="Value" />
</syncfusion:SfChart>
```

Useful links
- Syncfusion WPF documentation: https://www.syncfusion.com/documentation/wpf
- Syncfusion API reference / help: https://help.syncfusion.com/

How AI (Copilot) can use these files
- Copilot Chat reads `.github/instructions` and `docs/` files when repo-instructions enabled and will prioritize repository-specific guidance.
- Having local examples + doc file helps the AI provide accurate code snippets tailored to project conventions.

Next recommended steps
- Add a few more targeted code snippets for common tasks (theme switch, grid setup, chart update with MemoryOptimizedDataSet).
- Consider downloading or mirroring key Syncfusion API pages (or add links) if offline access is required.
- Optionally enable source/symbols for NuGet packages so IDE/tools can resolve signatures more accurately.
