using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using MGK_Analyzer.Models;

namespace MGK_Analyzer.Services
{
    public class FileExplorerService
    {
        private static FileExplorerService? _instance;
        public static FileExplorerService Instance => _instance ??= new FileExplorerService();

        private AppSettings _settings;

        private FileExplorerService()
        {
            _settings = AppSettings.Load();
        }

        public string GetDefaultPath()
        {
            // 마지막 사용 경로가 있고 유효하면 반환
            if (!string.IsNullOrEmpty(_settings.LastFileExplorerPath) && 
                Directory.Exists(_settings.LastFileExplorerPath))
            {
                return _settings.LastFileExplorerPath;
            }

            // 기본값: 사용자 디렉토리
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        public void SaveLastPath(string path)
        {
            _settings.LastFileExplorerPath = path;
            _settings.Save();
        }

        public void PopulateTreeView(TreeView treeView, string rootPath = null)
        {
            try
            {
                treeView.Items.Clear();

                if (string.IsNullOrEmpty(rootPath))
                {
                    rootPath = GetDefaultPath();
                }

                var rootItem = CreateTreeViewItem(rootPath, true);
                treeView.Items.Add(rootItem);

                // 루트 항목 확장
                rootItem.IsExpanded = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error populating tree view: {ex.Message}");
            }
        }

        private TreeViewItem CreateTreeViewItem(string path, bool isRoot = false)
        {
            var item = new TreeViewItem();
            
            try
            {
                var directoryInfo = new DirectoryInfo(path);
                
                // 헤더 설정
                var panel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
                
                // 아이콘 추가
                var icon = new System.Windows.Controls.TextBlock 
                { 
                    Text = GetFolderIcon(path), 
                    FontSize = 12, 
                    Margin = new System.Windows.Thickness(0, 0, 5, 0) 
                };
                
                // 폴더명 추가
                var name = new System.Windows.Controls.TextBlock 
                { 
                    Text = isRoot ? directoryInfo.FullName : directoryInfo.Name,
                    FontSize = 11
                };
                
                panel.Children.Add(icon);
                panel.Children.Add(name);
                item.Header = panel;
                item.Tag = path;

                // 하위 디렉토리가 있는지 확인하여 확장 가능 표시
                if (HasSubDirectories(path))
                {
                    item.Items.Add("Loading...");
                }

                // 지연 로딩을 위한 이벤트 핸들러
                item.Expanded += TreeViewItem_Expanded;
                
                // 선택 이벤트 핸들러 (경로 저장용)
                item.Selected += (s, e) => SaveLastPath(path);
            }
            catch (Exception ex)
            {
                item.Header = $"Error: {Path.GetFileName(path)}";
                System.Diagnostics.Debug.WriteLine($"Error creating tree item for {path}: {ex.Message}");
            }

            return item;
        }

        private void TreeViewItem_Expanded(object sender, System.Windows.RoutedEventArgs e)
        {
            var item = sender as TreeViewItem;
            if (item?.Tag is string path && item.Items.Count == 1 && item.Items[0] is string)
            {
                item.Items.Clear();
                LoadSubDirectoriesAndFiles(item, path);
            }
        }

        private void LoadSubDirectoriesAndFiles(TreeViewItem parentItem, string parentPath)
        {
            try
            {
                // 디렉토리 먼저 로드
                var directories = Directory.GetDirectories(parentPath)
                    .Where(d => !IsHiddenOrSystem(d))
                    .OrderBy(d => Path.GetFileName(d))
                    .ToList();

                foreach (var directory in directories)
                {
                    var subItem = CreateTreeViewItem(directory);
                    parentItem.Items.Add(subItem);
                }

                // 파일들 로드 (일반적인 파일 형식만)
                var files = Directory.GetFiles(parentPath)
                    .Where(f => !IsHiddenOrSystem(f) && IsDisplayableFile(f))
                    .OrderBy(f => Path.GetFileName(f))
                    .ToList();

                foreach (var file in files)
                {
                    var fileItem = CreateFileTreeViewItem(file);
                    parentItem.Items.Add(fileItem);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // 접근 권한 없는 폴더는 무시
                var accessDeniedItem = new TreeViewItem 
                { 
                    Header = CreateErrorHeader("??", "Access Denied"),
                    IsEnabled = false 
                };
                parentItem.Items.Add(accessDeniedItem);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading subdirectories for {parentPath}: {ex.Message}");
            }
        }

        private TreeViewItem CreateFileTreeViewItem(string filePath)
        {
            var item = new TreeViewItem();
            
            try
            {
                var fileInfo = new FileInfo(filePath);
                
                // 헤더 설정
                var panel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
                
                // 파일 아이콘
                var icon = new System.Windows.Controls.TextBlock 
                { 
                    Text = GetFileIcon(filePath), 
                    FontSize = 12, 
                    Margin = new System.Windows.Thickness(0, 0, 5, 0) 
                };
                
                // 파일명
                var name = new System.Windows.Controls.TextBlock 
                { 
                    Text = fileInfo.Name,
                    FontSize = 11,
                    Foreground = System.Windows.Media.Brushes.DarkBlue
                };
                
                panel.Children.Add(icon);
                panel.Children.Add(name);
                item.Header = panel;
                item.Tag = filePath;
                
                // 파일 더블클릭 이벤트 (나중에 구현 예정)
                item.MouseDoubleClick += (s, e) => 
                {
                    System.Diagnostics.Debug.WriteLine($"File double-clicked: {filePath}");
                    e.Handled = true;
                };
            }
            catch (Exception ex)
            {
                item.Header = CreateErrorHeader("?", $"Error: {Path.GetFileName(filePath)}");
                System.Diagnostics.Debug.WriteLine($"Error creating file tree item for {filePath}: {ex.Message}");
            }

            return item;
        }

        private System.Windows.Controls.StackPanel CreateErrorHeader(string icon, string text)
        {
            var panel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            
            panel.Children.Add(new System.Windows.Controls.TextBlock 
            { 
                Text = icon, 
                FontSize = 12, 
                Margin = new System.Windows.Thickness(0, 0, 5, 0) 
            });
            
            panel.Children.Add(new System.Windows.Controls.TextBlock 
            { 
                Text = text,
                FontSize = 11,
                Foreground = System.Windows.Media.Brushes.Gray
            });
            
            return panel;
        }

        private bool HasSubDirectories(string path)
        {
            try
            {
                return Directory.GetDirectories(path).Any(d => !IsHiddenOrSystem(d));
            }
            catch
            {
                return false;
            }
        }

        private bool IsHiddenOrSystem(string path)
        {
            try
            {
                var attributes = File.GetAttributes(path);
                return (attributes & FileAttributes.Hidden) != 0 || 
                       (attributes & FileAttributes.System) != 0;
            }
            catch
            {
                return false;
            }
        }

        private bool IsDisplayableFile(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLower();
            var displayableExtensions = new HashSet<string>
            {
                // 텍스트 파일
                ".txt", ".md", ".log", ".csv", ".json", ".xml", ".html", ".htm",
                
                // 프로그래밍 파일
                ".cs", ".js", ".ts", ".py", ".java", ".cpp", ".c", ".h", ".css",
                ".sql", ".xaml", ".config", ".ini", ".yaml", ".yml",
                
                // 오피스 문서
                ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".pdf",
                
                // 이미지 파일
                ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".ico", ".svg",
                
                // 압축 파일
                ".zip", ".rar", ".7z", ".tar", ".gz",
                
                // 실행 파일
                ".exe", ".msi", ".bat", ".cmd", ".ps1"
            };

            return displayableExtensions.Contains(extension);
        }

        private string GetFolderIcon(string path)
        {
            try
            {
                // 특별한 폴더들에 대한 아이콘
                var folderName = Path.GetFileName(path).ToLower();
                
                return folderName switch
                {
                    "desktop" => "???",
                    "documents" => "??",
                    "downloads" => "??",
                    "pictures" => "???",
                    "music" => "??",
                    "videos" => "??",
                    "onedrive" => "??",
                    "dropbox" => "??",
                    "bin" or "debug" or "release" => "??",
                    "obj" => "??",
                    ".git" => "??",
                    ".vs" or ".vscode" => "??",
                    "node_modules" => "??",
                    _ => "??"
                };
            }
            catch
            {
                return "??";
            }
        }

        private string GetFileIcon(string filePath)
        {
            try
            {
                var extension = Path.GetExtension(filePath).ToLower();
                
                return extension switch
                {
                    // 텍스트 파일
                    ".txt" => "??",
                    ".md" => "??",
                    ".log" => "??",
                    
                    // 데이터 파일
                    ".csv" => "??",
                    ".json" => "???",
                    ".xml" => "???",
                    ".yaml" or ".yml" => "??",
                    
                    // 웹 파일
                    ".html" or ".htm" => "??",
                    ".css" => "??",
                    ".js" => "?",
                    ".ts" => "??",
                    
                    // 프로그래밍 파일
                    ".cs" => "??",
                    ".py" => "??",
                    ".java" => "?",
                    ".cpp" or ".c" => "??",
                    ".h" => "??",
                    ".sql" => "???",
                    ".xaml" => "???",
                    
                    // 설정 파일
                    ".config" => "??",
                    ".ini" => "??",
                    
                    // 오피스 문서
                    ".doc" or ".docx" => "??",
                    ".xls" or ".xlsx" => "??",
                    ".ppt" or ".pptx" => "???",
                    ".pdf" => "??",
                    
                    // 이미지 파일
                    ".jpg" or ".jpeg" => "???",
                    ".png" => "???",
                    ".gif" => "???",
                    ".bmp" => "???",
                    ".ico" => "??",
                    ".svg" => "??",
                    
                    // 압축 파일
                    ".zip" => "???",
                    ".rar" => "??",
                    ".7z" => "??",
                    ".tar" or ".gz" => "??",
                    
                    // 실행 파일
                    ".exe" => "??",
                    ".msi" => "??",
                    ".bat" or ".cmd" => "?",
                    ".ps1" => "??",
                    
                    // 기본
                    _ => "??"
                };
            }
            catch
            {
                return "??";
            }
        }

        public void RefreshTreeView(TreeView treeView, string currentPath = null)
        {
            PopulateTreeView(treeView, currentPath);
        }
    }
}