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
                var panel = new StackPanel { Orientation = Orientation.Horizontal };
                
                // 아이콘 추가
                var icon = new TextBlock 
                { 
                    Text = GetFolderIcon(path), 
                    FontSize = 12, 
                    Margin = new System.Windows.Thickness(0, 0, 5, 0) 
                };
                
                // 폴더명 추가
                var name = new TextBlock 
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
                LoadSubDirectories(item, path);
            }
        }

        private void LoadSubDirectories(TreeViewItem parentItem, string parentPath)
        {
            try
            {
                var directories = Directory.GetDirectories(parentPath)
                    .OrderBy(d => Path.GetFileName(d))
                    .ToList();

                foreach (var directory in directories)
                {
                    // 숨김 폴더 제외 (선택적)
                    var dirInfo = new DirectoryInfo(directory);
                    if ((dirInfo.Attributes & FileAttributes.Hidden) == 0)
                    {
                        var subItem = CreateTreeViewItem(directory);
                        parentItem.Items.Add(subItem);
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // 접근 권한 없는 폴더는 무시
                var accessDeniedItem = new TreeViewItem 
                { 
                    Header = "Access Denied",
                    IsEnabled = false 
                };
                parentItem.Items.Add(accessDeniedItem);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading subdirectories for {parentPath}: {ex.Message}");
            }
        }

        private bool HasSubDirectories(string path)
        {
            try
            {
                return Directory.GetDirectories(path).Any(d => 
                {
                    var dirInfo = new DirectoryInfo(d);
                    return (dirInfo.Attributes & FileAttributes.Hidden) == 0;
                });
            }
            catch
            {
                return false;
            }
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