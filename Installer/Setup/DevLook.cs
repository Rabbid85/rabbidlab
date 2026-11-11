using System.Windows;

namespace RabbidLab.Setup
{
    /// <summary>
    /// What tells Rabbid Lab Dev's Setup apart from the release's: a DEV tag in the title bar and beside the heading. Its
    /// colours are the release's (App.xaml), exactly; only the tags and its icon (the release's, with a DEV band) differ.
    /// </summary>
    internal static class DevLook
    {
        public static void Window(MainWindow window)
        {
            window.DevTag.Visibility = Visibility.Visible;
            window.HeadDevTag.Visibility = Visibility.Visible;
        }
    }
}
