using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace XPower
{
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            InitializeComponent();
        }

        private void Navigation_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null) return;

            string section = button.Tag as string ?? "Home";
            PageTitle.Text = section == "Home" ? "Welcome to X-Power" : section;
            if (section == "Files")
            {
                // The real StorageFolder/file-picker workflow is intentionally deferred
                // until the foundation build is verified on Xbox hardware.
                PageTitle.Text = "Files — foundation view";
            }
        }
    }
}
