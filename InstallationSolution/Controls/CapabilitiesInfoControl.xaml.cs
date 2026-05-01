using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using System.Collections.Generic;

namespace InstallationSolution.Controls
{
    public sealed partial class CapabilitiesInfoControl : UserControl
    {
        public static readonly DependencyProperty HeadTextProperty =
            DependencyProperty.Register(nameof(HeadText), typeof(string), typeof(CapabilitiesInfoControl),
                new PropertyMetadata(default(string), (d, _) => ((CapabilitiesInfoControl)d).HeaderTextBlock.Text = (string)d.GetValue(HeadTextProperty)));

        public static readonly DependencyProperty CapabilitiesListProperty =
            DependencyProperty.Register(nameof(CapabilitiesList), typeof(List<string>), typeof(CapabilitiesInfoControl),
                new PropertyMetadata(default(List<string>), (d, _) => ((CapabilitiesInfoControl)d).Rebuild()));

        public string HeadText
        {
            get => (string)GetValue(HeadTextProperty);
            set => SetValue(HeadTextProperty, value);
        }

        public List<string> CapabilitiesList
        {
            get => (List<string>)GetValue(CapabilitiesListProperty);
            set => SetValue(CapabilitiesListProperty, value);
        }

        public CapabilitiesInfoControl() => InitializeComponent();

        private void Rebuild()
        {
            if (CapabilitiesList == null) return;
            RichTextBlockCapabilities.Blocks.Clear();
            RichTextBlockFullCapabilities.Blocks.Clear();

            foreach (var cap in CapabilitiesList)
            {
                if (string.IsNullOrWhiteSpace(cap)) continue;
                var para = new Paragraph();
                para.Inlines.Add(new Run { Text = $"• {cap}" });
                RichTextBlockFullCapabilities.Blocks.Add(para);

                if (RichTextBlockCapabilities.Blocks.Count < 3)
                {
                    var para2 = new Paragraph();
                    para2.Inlines.Add(new Run { Text = $"• {cap}" });
                    RichTextBlockCapabilities.Blocks.Add(para2);
                }
            }

            MoreButton.Content = "显示更多";
            MoreButton.Visibility = RichTextBlockFullCapabilities.Blocks.Count > 3
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void MoreButton_Click(object sender, RoutedEventArgs e)
        {
            MoreButton.Visibility = Visibility.Collapsed;
            Root.BorderThickness = new Thickness(1, 0, 0, 0);
            RichTextBlockCapabilities.Visibility = Visibility.Collapsed;
            RichTextBlockFullCapabilities.Visibility = Visibility.Visible;
            CapabilitiesHeight.Height = new GridLength(1, GridUnitType.Star);
            _ = RichTextBlockFullCapabilities.Focus(FocusState.Pointer);
        }

        private void Root_LostFocus(object sender, RoutedEventArgs e)
        {
            MoreButton.Visibility = Visibility.Visible;
            Root.BorderThickness = new Thickness(0);
            RichTextBlockCapabilities.Visibility = Visibility.Visible;
            RichTextBlockFullCapabilities.Visibility = Visibility.Collapsed;
            CapabilitiesHeight.Height = GridLength.Auto;
        }
    }
}
