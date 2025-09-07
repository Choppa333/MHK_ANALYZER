using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Media;
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
                
                // ���� ������ (�̹��� ���)
                var iconElement = CreateFolderIcon(path);
                
                // ������ �߰�
                var name = new System.Windows.Controls.TextBlock 
                { 
                    Text = isRoot ? directoryInfo.FullName : directoryInfo.Name,
                    FontSize = 11,
                    Margin = new System.Windows.Thickness(5, 0, 0, 0),
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };
                
                panel.Children.Add(iconElement);
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
                    Header = CreateErrorHeader("[X]", "Access Denied"),
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
                var iconElement = CreateFileIcon(filePath);
                
                // ���ϸ�
                var name = new System.Windows.Controls.TextBlock 
                { 
                    Text = fileInfo.Name,
                    FontSize = 11,
                    Foreground = System.Windows.Media.Brushes.DarkBlue,
                    Margin = new System.Windows.Thickness(5, 0, 0, 0),
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };
                
                panel.Children.Add(iconElement);
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
                item.Header = CreateErrorHeader("[!]", $"Error: {Path.GetFileName(filePath)}");
                System.Diagnostics.Debug.WriteLine($"Error creating file tree item for {filePath}: {ex.Message}");
            }

            return item;
        }

        private System.Windows.FrameworkElement CreateFolderIcon(string path)
        {
            var icon = new System.Windows.Controls.TextBlock
            {
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14,
                Width = 16,
                Height = 16,
                TextAlignment = System.Windows.TextAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Foreground = GetFolderIconColor(path)
            };

            icon.Text = GetFolderIconText(path);
            return icon;
        }

        private System.Windows.FrameworkElement CreateFileIcon(string filePath)
        {
            var icon = new System.Windows.Controls.TextBlock
            {
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12,
                Width = 16,
                Height = 16,
                TextAlignment = System.Windows.TextAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Foreground = GetFileIconColor(filePath)
            };

            icon.Text = GetFileIconText(filePath);
            return icon;
        }

        private string GetFolderIconText(string path)
        {
            try
            {
                var folderName = Path.GetFileName(path).ToLower();
                
                return folderName switch
                {
                    "desktop" => "DT",
                    "documents" => "DC",
                    "downloads" => "DL",
                    "pictures" => "PIC",
                    "music" => "MUS",
                    "videos" => "VID",
                    "onedrive" => "OD",
                    "dropbox" => "DB",
                    "bin" or "debug" or "release" => "BIN",
                    "obj" => "OBJ",
                    ".git" => "GIT",
                    ".vs" or ".vscode" => "IDE",
                    "node_modules" => "NPM",
                    _ => "DIR"
                };
            }
            catch
            {
                return "DIR";
            }
        }

        private string GetFileIconText(string filePath)
        {
            try
            {
                var extension = Path.GetExtension(filePath).ToLower();
                
                return extension switch
                {
                    // �ؽ�Ʈ ����
                    ".txt" => "TXT",
                    ".md" => "MD",
                    ".log" => "LOG",
                    
                    // ������ ����
                    ".csv" => "CSV",
                    ".json" => "JSON",
                    ".xml" => "XML",
                    ".yaml" or ".yml" => "YML",
                    
                    // �� ����
                    ".html" or ".htm" => "HTML",
                    ".css" => "CSS",
                    ".js" => "JS",
                    ".ts" => "TS",
                    
                    // ���α׷��� ����
                    ".cs" => "C#",
                    ".py" => "PY",
                    ".java" => "JAVA",
                    ".cpp" or ".c" => "C++",
                    ".h" => "H",
                    ".sql" => "SQL",
                    ".xaml" => "XAML",
                    
                    // ���� ����
                    ".config" => "CFG",
                    ".ini" => "INI",
                    
                    // ���ǽ� ����
                    ".doc" or ".docx" => "DOC",
                    ".xls" or ".xlsx" => "XLS",
                    ".ppt" or ".pptx" => "PPT",
                    ".pdf" => "PDF",
                    
                    // �̹��� ����
                    ".jpg" or ".jpeg" => "JPG",
                    ".png" => "PNG",
                    ".gif" => "GIF",
                    ".bmp" => "BMP",
                    ".ico" => "ICO",
                    ".svg" => "SVG",
                    
                    // ���� ����
                    ".zip" => "ZIP",
                    ".rar" => "RAR",
                    ".7z" => "7Z",
                    ".tar" or ".gz" => "TAR",
                    
                    // ���� ����
                    ".exe" => "EXE",
                    ".msi" => "MSI",
                    ".bat" or ".cmd" => "BAT",
                    ".ps1" => "PS1",
                    
                    // �⺻
                    _ => "FILE"
                };
            }
            catch
            {
                return "FILE";
            }
        }

        private Brush GetFolderIconColor(string path)
        {
            try
            {
                var folderName = Path.GetFileName(path).ToLower();
                
                return folderName switch
                {
                    "desktop" => Brushes.Blue,
                    "documents" => Brushes.DarkGreen,
                    "downloads" => Brushes.Orange,
                    "pictures" => Brushes.Purple,
                    "music" => Brushes.DeepPink,
                    "videos" => Brushes.DarkRed,
                    "onedrive" => Brushes.SkyBlue,
                    "dropbox" => Brushes.Blue,
                    "bin" or "debug" or "release" => Brushes.Gray,
                    "obj" => Brushes.LightGray,
                    ".git" => Brushes.OrangeRed,
                    ".vs" or ".vscode" => Brushes.Blue,
                    "node_modules" => Brushes.Green,
                    _ => Brushes.Goldenrod
                };
            }
            catch
            {
                return Brushes.Goldenrod;
            }
        }

        private Brush GetFileIconColor(string filePath)
        {
            try
            {
                var extension = Path.GetExtension(filePath).ToLower();
                
                return extension switch
                {
                    // �ؽ�Ʈ ����
                    ".txt" or ".md" or ".log" => Brushes.Black,
                    
                    // ������ ����
                    ".csv" or ".json" or ".xml" or ".yaml" or ".yml" => Brushes.DarkBlue,
                    
                    // �� ����
                    ".html" or ".htm" => Brushes.Orange,
                    ".css" => Brushes.Blue,
                    ".js" => Brushes.Gold,
                    ".ts" => Brushes.DarkBlue,
                    
                    // ���α׷��� ����
                    ".cs" => Brushes.Purple,
                    ".py" => Brushes.Green,
                    ".java" => Brushes.DarkOrange,
                    ".cpp" or ".c" or ".h" => Brushes.DarkSlateBlue,
                    ".sql" => Brushes.Blue,
                    ".xaml" => Brushes.Purple,
                    
                    // ���� ����
                    ".config" or ".ini" => Brushes.Gray,
                    
                    // ���ǽ� ����
                    ".doc" or ".docx" => Brushes.DarkBlue,
                    ".xls" or ".xlsx" => Brushes.Green,
                    ".ppt" or ".pptx" => Brushes.Orange,
                    ".pdf" => Brushes.DarkRed,
                    
                    // �̹��� ����
                    ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".ico" or ".svg" => Brushes.Purple,
                    
                    // ���� ����
                    ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => Brushes.Brown,
                    
                    // ���� ����
                    ".exe" or ".msi" => Brushes.Red,
                    ".bat" or ".cmd" or ".ps1" => Brushes.DarkGreen,
                    
                    // �⺻
                    _ => Brushes.Black
                };
            }
            catch
            {
                return Brushes.Black;
            }
        }

        private System.Windows.Controls.StackPanel CreateErrorHeader(string icon, string text)
        {
            var panel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            
            panel.Children.Add(new System.Windows.Controls.TextBlock 
            { 
                Text = icon, 
                FontSize = 12, 
                Margin = new System.Windows.Thickness(0, 0, 5, 0),
                Foreground = Brushes.Red
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

        public void RefreshTreeView(TreeView treeView, string currentPath = null)
        {
            PopulateTreeView(treeView, currentPath);
        }
    }
}