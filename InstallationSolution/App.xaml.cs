using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
using System.Linq;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Resources;
using InstallationSolution.Pages;
using AppInstance = Microsoft.Windows.AppLifecycle.AppInstance;

namespace InstallationSolution
{
    public partial class App : Application
    {
        public static Window? MainWindow { get; private set; }
        public static string? MsixPath { get; set; }

        public App()
        {
            this.InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            MainWindow = new MainWindow();

            // 设置标题栏
            AppThemeManager.SetupTitleBar();

            // 激活窗口
            MainWindow.Activate();

            // 延后初始化UI
            MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                try 
                { 
                    MainWindow.AppWindow.SetIcon("Assets/AppIcon.ico"); 
                } 
                catch { }

                // 设置窗口标题
                try
                {
                    var loader = new ResourceLoader();
                    MainWindow.AppWindow.Title = loader.GetString("AppTitle");
                }
                catch
                {
                    try
                    {
                        MainWindow.AppWindow.Title = Package.Current.DisplayName;
                    }
                    catch
                    {
                        MainWindow.AppWindow.Title = "Installer";
                    }
                }

                // 根据启动方式决定显示哪个界面
                var activationArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
                Debug.WriteLine($"Activation Kind: {activationArgs.Kind}");
                
                // 检查是否是文件激活（双击文件）
                var filePathToLoad = ExtractFilePathFromActivation(activationArgs);
                Debug.WriteLine($"File path from activation: {filePathToLoad}");
                
                if (activationArgs.Kind == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.File && filePathToLoad != null)
                {
                    // 文件激活：显示选择界面（安装或生成）
                    Debug.WriteLine("Showing file choice page (file activation)");
                    ((MainWindow)MainWindow).ShowSplashForFileChoice(filePathToLoad);
                }
                else
                {
                    // 检查命令行参数（Guard传入）
                    var cmdArgs = Environment.GetCommandLineArgs();
                    if (cmdArgs.Length > 1)
                    {
                        MsixPath = cmdArgs[1];
                        Debug.WriteLine($"MsixPath from cmdArgs: {MsixPath}");
                    }
                    
                    if (!string.IsNullOrEmpty(MsixPath))
                    {
                        // 有命令行参数（Guard传入）：直接显示安装器界面
                        Debug.WriteLine("Showing installer UI (command line)");
                        ((MainWindow)MainWindow).ShowSplash();
                    }
                    else
                    {
                        // 无参数：显示生成器界面
                        Debug.WriteLine("Showing generator page");
                        ((MainWindow)MainWindow).ShowSplashForGenerator(null);
                    }
                }
            });
        }

        // ── 从激活参数中提取文件路径 ────────────────────────────────
        private static string? ExtractFilePathFromActivation(Microsoft.Windows.AppLifecycle.AppActivationArguments activationArgs)
        {
            try
            {
                Debug.WriteLine($"Activation Kind: {activationArgs.Kind}");
                
                if (activationArgs.Kind != Microsoft.Windows.AppLifecycle.ExtendedActivationKind.File)
                {
                    Debug.WriteLine("Not a file activation");
                    return null;
                }
                
                if (activationArgs.Data is not Windows.ApplicationModel.Activation.IFileActivatedEventArgs fileArgs)
                {
                    Debug.WriteLine("Data is not IFileActivatedEventArgs");
                    return null;
                }

                Debug.WriteLine($"Files count: {fileArgs.Files.Count}");
                
                var file = fileArgs.Files.OfType<Windows.Storage.StorageFile>().FirstOrDefault(f =>
                {
                    var ext = System.IO.Path.GetExtension(f.Name).ToLowerInvariant();
                    Debug.WriteLine($"File: {f.Name}, Extension: {ext}");
                    return ext is ".msix" or ".msixbundle" or ".appx" or ".appxbundle";
                });

                if (file != null)
                {
                    Debug.WriteLine($"Found file: {file.Path}");
                    return file.Path;
                }
                else
                {
                    Debug.WriteLine("No matching file found");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ExtractFilePathFromActivation failed: {ex.Message}");
                return null;
            }
        }
    }
}
