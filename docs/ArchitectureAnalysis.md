# MGK Analyzer 아키텍처 및 의존성 요약

_최종 업데이트: 2024-XX-XX (자동 생성)_

## 솔루션 구성
- `MGK_Analyzer.sln`에는 다음 세 프로젝트가 포함되어 있습니다.
  - `MGK_Analyzer` (`net8.0-windows` WPF, 주요 데스크톱 애플리케이션)
  - 레거시 `MGK_ANALYZER_SETUP.vdproj` (Visual Studio 설치 프로젝트, 참고용으로 보관)
  - `SampleFileGenerator` (별도 폴더에 위치, 현재 워크스페이스에 소스 없음)
- 추가로 WiX v3 설정 프로젝트(`MGK_Analyzer.Setup`)가 존재하며, `dotnet publish` → harvest → MSI 빌드를 자동화하여 win-x64 설치 패키지를 생성합니다.

## 애플리케이션 아키텍처 개요
1. **시작 & 설정**
   - `App.xaml.cs`에서 Syncfusion 라이선스를 등록하고 DEBUG 모드에서 콘솔을 할당합니다.
   - `ThemeManager`는 `%AppData%/MGK_Analyzer/settings.json`에 저장된 설정을 `AppSettings`를 통해 로드하고, Syncfusion 테마를 적용한 뒤 `ThemeChanged` 이벤트를 발행합니다.

2. **쉘(MainWindow)**
   - XAML은 메뉴/툴바/상태바/MDI용 `Canvas`를 정의합니다.
   - `MainWindow.xaml.cs`는 파일·프로젝트·시험 모드·컨투어/서피스 버튼·테마 UI·로그 뷰어 등을 `MdiWindowManager`와 연동합니다.
   - `PerformanceLogger`는 CSV 로딩·창 생성 등 장시간 작업을 계측하여 향후 로그 뷰어에서 활용할 수 있도록 합니다.

3. **MDI 인프라**
   - `MdiWindowManager`는 `MdiChartWindow`, `MdiContour2DWindow`, `MdiSurface3DWindow` 같은 사용자 컨트롤을 Canvas 위에 배치하고, 계단식/타일/전체 최소화 및 창 개수 업데이트를 담당합니다.
   - `MdiZOrderService`는 Z-Index를 단일 경로로 관리하여 `BringToFront` 효과를 제공합니다.

4. **시각화 컨트롤**
   - `MdiChartWindow`: Syncfusion `SfChart` 기반 시계열 차트 + 좌측 스냅샷/데이터 패널 + 우측 시리즈·크기·속성 관리 패널.
   - `MdiContour2DWindow`: OxyPlot HeatMap/Contour 조합으로 `EfficiencySampleData`를 시각화하며 팔레트/강도 슬라이더를 제공.
   - `MdiSurface3DWindow`: Syncfusion `SurfaceChart`에 IDW 보간, 팔레트 선택, Tilt/Rotate 슬라이더, 범례 생성을 포함.

5. **데이터 계층**
   - `MemoryOptimizedDataSet`/`SeriesData`는 연속 `float[]`/`BitArray` 버퍼로 CSV 데이터를 관리하여 GC 부담을 줄이고, 시간 축 계산 헬퍼를 제공합니다.
   - `ChartSeriesViewModel`(CommunityToolkit MVVM)은 시리즈 선택 상태를 UI(`ColumnSelectionWindow`)와 양방향으로 유지합니다.

6. **CSV 로딩**
   - `CsvDataLoader`는 메타데이터 제거(`#TYPE`, `#DATE`), 파일 크기·행 수 추정, 인코딩 판별(UTF-8 BOM/UTF-8/EUC-KR), 더블 버퍼 스트리밍 파싱, 진행률 보고, 통계 계산 비동기화를 순차적으로 수행합니다.
   - `TestDataCsvParser`는 시험 종류별 CSV 처리기를 위한 골격이며 아직 구현되지 않았습니다.

7. **보조 창**
   - `ThemeSettingsWindow`: 테마 미리보기/적용/취소/저장 플로우.
   - `ColumnSelectionWindow`: 차트 시리즈 다중 선택 UI.
   - `LogViewerWindow`: 코드에서는 참조되나 실제 구현은 미완(향후 `PerformanceLogger` 뷰어).

## 외부 패키지
| 패키지 | 버전 | 용도 | 비고 |
|--------|------|------|------|
| `CommunityToolkit.Mvvm` | 8.4.0 | MVVM 헬퍼(`ObservableObject`, 소스 제너레이터) | `ChartSeriesViewModel` 등에서 사용 |
| `Syncfusion.SfChart.WPF`, `SfGrid`, `SfInput`, `SfHeatMap`, `Tools`, Themes | 31.1.17 | 주요 차트/테마/툴 구성 요소 | `SfChart`, `SurfaceChart` 실사용, 나머지는 확장 대비 |
| `OxyPlot.Core` / `OxyPlot.Wpf` | 2.1.2 | 2D 컨투어/히트맵 | `MdiContour2DWindow` |
| `EPPlus` | 8.2.1 | Excel 내보내기/가져오기 | 향후 Export 기능용, 현재 직접 사용 없음 |
| `CsvHelper` | 30.0.1 | 시험용 CSV 파싱 | `TestDataCsvParser`에서 참조만 존재 |

## 데이터 흐름
1. 사용자가 메뉴/툴바에서 CSV를 선택합니다.
2. `MainWindow`가 `CsvDataLoader.LoadCsvDataAsync`를 호출하여 진행률을 표시하고 대용량 여부를 확인합니다.
3. 로더가 `MemoryOptimizedDataSet`을 반환하면 `MdiWindowManager.CreateChartWindow`가 Syncfusion 차트 창을 생성하여 바인딩합니다.
4. `효율맵2D/3D`, `NT-Curve` 버튼은 즉시 샘플 데이터를 생성해 시각화합니다.
5. `PerformanceLogger`가 각 작업의 타이밍을 기록하고, 추후 `LogViewerWindow`에서 볼 수 있도록 준비합니다.
6. MSI 배포 파이프라인: `MGK_Analyzer.Setup.wixproj` → `dotnet publish`(win-x64 self-contained) → WiX `heat.exe` harvest → `light.exe` 빌드 → 바로가기 포함 설치 프로그램 생성.

## 파악된 과제 / 다음 단계
- `TestDataCsvParser` 구현 및 `LoadTest`/`NoLoadTest` 메뉴와 연동.
- `PerformanceLogger.LogEntries`를 표시할 `LogViewerWindow` 작성.
- Syncfusion 라이선스 키를 환경 변수/보안 저장소 등 외부로 분리.
- WiX vs. `.vdproj` vs. NSIS 중 공식 배포 경로를 결정하고 불필요한 스크립트 정리.
- (선택) `.editorconfig`/`CONTRIBUTING.md`를 추가하여 코딩 규칙과 온보딩 절차 문서화.
