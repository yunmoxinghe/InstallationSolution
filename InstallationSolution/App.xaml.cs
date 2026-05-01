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
        public static string? MsixPath { get; private set; }

        public App()
        {
            this.InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            // 读取命令行参数
            var cmdArgs = Environment.GetCommandLineArgs();
            if (cmdArgs.Length > 1)
                MsixPath = cmdArgs[1];

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
                string? filePathToLoad = null;
                
                if (!string.IsNullOrEmpty(MsixPath))
                {
                    // 有参数：显示安装器界面（InstallerUI功能）
                    ((MainWindow)MainWindow).ShowSplash();
                }
                else
                {
                    // 检查是否是文件激活
                    filePathToLoad = ExtractFilePathFromActivation(AppInstance.GetCurrent().GetActivatedEventArgs());
                    
                    // 显示启动屏幕，然后导航到生成器
                    ((MainWindow)MainWindow).ShowSplashForGenerator(filePathToLoad);
                }
            });
        }

        // ── 从激活参数中提取文件路径 ────────────────────────────────
        private static string? ExtractFilePathFromActivation(Microsoft.Windows.AppLifecycle.AppActivationArguments activationArgs)
        {
            try
            {
                if (activationArgs.Kind != Microsoft.Windows.AppLifecycle.ExtendedActivationKind.File) return null;
                if (activationArgs.Data is not Windows.ApplicationModel.Activation.IFileActivatedEventArgs fileArgs) return null;

                var file = fileArgs.Files.OfType<Windows.Storage.StorageFile>().FirstOrDefault(f =>
                {
                    var ext = System.IO.Path.GetExtension(f.Name).ToLowerInvariant();
                    return ext is ".msix" or ".msixbundle" or ".appx" or ".appxbundle";
                });

                return file?.Path;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ExtractFilePathFromActivation failed: {ex.Message}");
                return null;
            }
        }
    }
}
