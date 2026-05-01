using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.IO;

namespace InstallationSolution.Pages
{
    public sealed partial class FileChoicePage : Page
    {
        private string? _filePath;

        public FileChoicePage()
        {
            this.InitializeComponent();
        }

        public void SetFilePath(string filePath)
        {
            _filePath = filePath;
            FileNameText.Text = Path.GetFileName(filePath);
        }

        private void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_filePath)) return;

            // 设置MsixPath并导航到安装界面
            App.MsixPath = _filePath;
            Frame.Navigate(typeof(HomePage));
        }

        private void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_filePath)) return;

            // 导航到生成器并加载文件
            Frame.Navigate(typeof(GeneratorPage));
            
            // 等待页面加载后设置文件
            DispatcherQueue.TryEnqueue(async () =>
            {
                await System.Threading.Tasks.Task.Delay(100);
                if (Frame.Content is GeneratorPage page)
                {
                    page.LoadFile(_filePath);
                }
            });
        }
    }
}
