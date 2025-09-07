# MGK Analyzer

A professional WPF data analysis application built with .NET 8 and Syncfusion components.

## ?? Features

### Core Functionality
- **Professional UI Layout**: Menu bar, toolbar, main workspace, and status bar
- **Data Management**: Import, export, analyze, and filter data
- **Project Management**: Create, open, and save projects
- **Theme System**: Multiple Syncfusion themes with live preview

### Theme Management
- **4 Available Themes**:
  - Material Light
  - Material Dark  
  - Fluent Light
  - Default (Windows)
- **Live Preview**: See theme changes instantly
- **Persistent Settings**: Theme selection saved between sessions
- **Easy Access**: Theme button in toolbar for quick switching

## ?? Technology Stack

- **.NET 8**: Latest .NET framework
- **WPF**: Windows Presentation Foundation for rich desktop UI
- **Syncfusion Community**: Professional UI components
  - SfDataGrid for data display
  - SfSkinManager for theme management
  - MaterialLight, MaterialDark, FluentLight themes

## ?? Architecture

```
MGK_Analyzer/
戍式式 Models/
弛   戌式式 AppSettings.cs          # Application settings persistence
戍式式 Services/
弛   戌式式 ThemeManager.cs         # Theme management service
戍式式 Views/
弛   戌式式 ThemeSettingsWindow.*   # Theme selection dialog
戍式式 MainWindow.*                # Main application window
戌式式 App.*                       # Application entry point
```

## ?? Getting Started

### Prerequisites
- Visual Studio 2022 or later
- .NET 8 SDK
- Syncfusion Community License (free)

### Installation
1. Clone the repository
   ```sh
   git clone https://github.com/YOUR_USERNAME/MGK_Analyzer.git
   ```
2. Open `MGK_Analyzer.sln` in Visual Studio
3. Restore NuGet packages
4. Build and run the application

### Syncfusion License Setup
1. Register for a free Syncfusion Community License
2. Replace the license key in `App.xaml.cs`:
   ```csharp
   SyncfusionLicenseProvider.RegisterLicense("YOUR_LICENSE_KEY");
   ```

## ?? Usage

### Theme Management
1. Click the ?? **Theme** button in the toolbar
2. Select any theme to **preview** it instantly
3. Click **"Apply & Save"** to make it permanent
4. Use **"Cancel"** to revert to the original theme

### Data Operations
- **Import Data**: ?? Import button or File ⊥ Data Import
- **Export Data**: ?? Export button or File ⊥ Data Export  
- **Analyze**: ?? Analysis tools and visualizations
- **Projects**: ??? Save and manage analysis projects

## ?? Theme System Details

The application uses Syncfusion's theme system with the following features:

- **Immediate Preview**: Themes apply instantly when selected
- **Persistent Storage**: Settings saved to `%AppData%\MGK_Analyzer\settings.json`
- **Fallback Handling**: Graceful handling of theme loading errors
- **Visual Feedback**: Clear indication of current vs. preview themes

## ?? Development

### Adding New Themes
1. Install additional Syncfusion theme packages
2. Update `ThemeManager.ThemeType` enum
3. Add theme to `GetAvailableThemes()` method
4. Update switch statements in `ApplyThemeInternal()`

### Project Structure
- **MVVM Pattern**: Clean separation of concerns
- **Service Layer**: Centralized business logic
- **Settings Management**: JSON-based configuration
- **Error Handling**: Comprehensive exception management

## ?? License

This project uses Syncfusion Community License for UI components.

## ?? Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## ?? Support

For questions and support, please open an issue in the GitHub repository.

---

**MGK Analyzer** - Professional Data Analysis Made Simple ???