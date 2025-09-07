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
            // ������ ��� ��ΰ� �ְ� ��ȿ�ϸ� ��ȯ
            if (!string.IsNullOrEmpty(_settings.LastFileExplorerPath) && 
                Directory.Exists(_settings.LastFileExplorerPath))
            {
                return _settings.LastFileExplorerPath;
            }

            // �⺻��: ����� ���丮
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

                // ��Ʈ �׸� Ȯ��
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
                
                // ��� ����
                var panel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
                
                // ������ �߰�
                var icon = new System.Windows.Controls.TextBlock 
                { 
                    Text = GetFolderIcon(path), 
                    FontSize = 12, 
                    Margin = new System.Windows.Thickness(0, 0, 5, 0) 
                };
                
                // ������ �߰�
                var name = new System.Windows.Controls.TextBlock 
                { 
                    Text = isRoot ? directoryInfo.FullName : directoryInfo.Name,
                    FontSize = 11
                };
                
                panel.Children.Add(icon);
                panel.Children.Add(name);
                item.Header = panel;
                item.Tag = path;

                // ���� ���丮�� �ִ��� Ȯ���Ͽ� Ȯ�� ���� ǥ��
                if (HasSubDirectories(path))
                {
                    item.Items.Add("Loading...");
                }

                // ���� �ε��� ���� �̺�Ʈ �ڵ鷯
                item.Expanded += TreeViewItem_Expanded;
                
                // ���� �̺�Ʈ �ڵ鷯 (��� �����)
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
                // ���丮 ���� �ε�
                var directories = Directory.GetDirectories(parentPath)
                    .Where(d => !IsHiddenOrSystem(d))
                    .OrderBy(d => Path.GetFileName(d))
                    .ToList();

                foreach (var directory in directories)
                {
                    var subItem = CreateTreeViewItem(directory);
                    parentItem.Items.Add(subItem);
                }

                // ���ϵ� �ε� (�Ϲ����� ���� ���ĸ�)
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
                // ���� ���� ���� ������ ����
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
                
                // ��� ����
                var panel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
                
                // ���� ������
                var icon = new System.Windows.Controls.TextBlock 
                { 
                    Text = GetFileIcon(filePath), 
                    FontSize = 12, 
                    Margin = new System.Windows.Thickness(0, 0, 5, 0) 
                };
                
                // ���ϸ�
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
                
                // ���� ����Ŭ�� �̺�Ʈ (���߿� ���� ����)
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
                // �ؽ�Ʈ ����
                ".txt", ".md", ".log", ".csv", ".json", ".xml", ".html", ".htm",
                
                // ���α׷��� ����
                ".cs", ".js", ".ts", ".py", ".java", ".cpp", ".c", ".h", ".css",
                ".sql", ".xaml", ".config", ".ini", ".yaml", ".yml",
                
                // ���ǽ� ����
                ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".pdf",
                
                // �̹��� ����
                ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".ico", ".svg",
                
                // ���� ����
                ".zip", ".rar", ".7z", ".tar", ".gz",
                
                // ���� ����
                ".exe", ".msi", ".bat", ".cmd", ".ps1"
            };

            return displayableExtensions.Contains(extension);
        }

        private string GetFolderIcon(string path)
        {
            try
            {
                // Ư���� �����鿡 ���� ������
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
                    // �ؽ�Ʈ ����
                    ".txt" => "??",
                    ".md" => "??",
                    ".log" => "??",
                    
                    // ������ ����
                    ".csv" => "??",
                    ".json" => "???",
                    ".xml" => "???",
                    ".yaml" or ".yml" => "??",
                    
                    // �� ����
                    ".html" or ".htm" => "??",
                    ".css" => "??",
                    ".js" => "?",
                    ".ts" => "??",
                    
                    // ���α׷��� ����
                    ".cs" => "??",
                    ".py" => "??",
                    ".java" => "?",
                    ".cpp" or ".c" => "??",
                    ".h" => "??",
                    ".sql" => "???",
                    ".xaml" => "???",
                    
                    // ���� ����
                    ".config" => "??",
                    ".ini" => "??",
                    
                    // ���ǽ� ����
                    ".doc" or ".docx" => "??",
                    ".xls" or ".xlsx" => "??",
                    ".ppt" or ".pptx" => "???",
                    ".pdf" => "??",
                    
                    // �̹��� ����
                    ".jpg" or ".jpeg" => "???",
                    ".png" => "???",
                    ".gif" => "???",
                    ".bmp" => "???",
                    ".ico" => "??",
                    ".svg" => "??",
                    
                    // ���� ����
                    ".zip" => "???",
                    ".rar" => "??",
                    ".7z" => "??",
                    ".tar" or ".gz" => "??",
                    
                    // ���� ����
                    ".exe" => "??",
                    ".msi" => "??",
                    ".bat" or ".cmd" => "?",
                    ".ps1" => "??",
                    
                    // �⺻
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