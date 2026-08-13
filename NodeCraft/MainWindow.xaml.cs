using CommonControls.WPF;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.Pages;
using NodeCraft.Plugins;

namespace NodeCraft
{
    public partial class MainWindow : FramelessWindowEx
    {
        private readonly FlowPage FlowEditor;
        private bool _notificationServiceRegistered;
        private bool _pluginFailureNotificationShown;
        private bool _startupGraphLoaded;

        public MainWindow(FlowPage flowPage)
        {
            InitializeComponent();
            FlowEditor = flowPage;
            RootGrid.Children.Add(FlowEditor);
            Grid.SetRow(FlowEditor, 1);
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_notificationServiceRegistered)
            {
                NotificationService.Register(this);
                _notificationServiceRegistered = true;
            }

            if (!_pluginFailureNotificationShown
                && App.PluginLoadReport.Failures.Count > 0)
            {
                NotificationService.ShowNotification(
                    "nodecraft-plugin-startup",
                    PluginStartupNotification.BuildMessage(App.PluginLoadReport.Failures),
                    5000);
                _pluginFailureNotificationShown = true;
            }

            if (_startupGraphLoaded)
                return;

            _startupGraphLoaded = true;
            var path = (Application.Current as App)?.StartupGraphFilePath;
            if (!string.IsNullOrWhiteSpace(path))
                FlowEditor.TryLoadGraphFile(path);
        }

        private void MainWindow_Closed(object sender, System.EventArgs e)
        {
            NotificationService.Unregister();
        }

        private void MenuNewGraph_Click(object sender, RoutedEventArgs e) => FlowEditor.NewGraph();

        private void MenuClearGraph_Click(object sender, RoutedEventArgs e) => FlowEditor.ClearGraph();

        private void MenuLoadGraph_Click(object sender, RoutedEventArgs e) => FlowEditor.LoadGraph();

        private void MenuSaveGraph_Click(object sender, RoutedEventArgs e) => FlowEditor.SaveGraph();

        private void MenuSaveGraphAs_Click(object sender, RoutedEventArgs e) => FlowEditor.SaveGraphAs();

        private void MenuValidateGraph_Click(object sender, RoutedEventArgs e) => FlowEditor.ValidateGraph();

        private void MenuRunGraph_Click(object sender, RoutedEventArgs e) => FlowEditor.RunGraph();

        private void MenuExit_Click(object sender, RoutedEventArgs e) => Close();

        private void DarkThemeMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            ChangeTheme(CommonControlTheme.BaseTheme.Dark);
        }

        private void DarkThemeMenuItem_Unchecked(object sender, RoutedEventArgs e)
        {
            ChangeTheme(CommonControlTheme.BaseTheme.Light);
        }

        private static void ChangeTheme(CommonControlTheme.BaseTheme theme)
        {
            var mergedDictionaries = Application.Current?.Resources?.MergedDictionaries;
            if (mergedDictionaries == null)
                return;

            foreach (var dictionary in mergedDictionaries)
            {
                if (dictionary is CommonControlTheme controlTheme)
                {
                    controlTheme.Theme = theme;
                    return;
                }
            }
        }
    }
}
