using CommonControls.WPF;
using System;
using System.ComponentModel;
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
        private bool _allowClose;

        public MainWindow(FlowPage flowPage)
        {
            InitializeComponent();
            FlowEditor = flowPage;
            FlowEditor.ExecutionStateChanged += FlowEditor_ExecutionStateChanged;
            RootGrid.Children.Add(FlowEditor);
            Grid.SetRow(FlowEditor, 1);
            UpdateExecutionCommandState();
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

            UpdateExecutionCommandState();
        }

        private void FlowEditor_ExecutionStateChanged(object sender, EventArgs e)
        {
            UpdateExecutionCommandState();
        }

        private void MainWindow_Closed(object sender, System.EventArgs e)
        {
            FlowEditor.ExecutionStateChanged -= FlowEditor_ExecutionStateChanged;
            NotificationService.Unregister();
        }

        private async void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (_allowClose || !FlowEditor.IsExecutionActive)
            {
                return;
            }

            e.Cancel = true;
            try
            {
                await FlowEditor.StopExecutionAsync();
            }
            catch (Exception exception)
            {
                NotificationService.ShowNotification(
                    "nodecraft-flow-close",
                    $"停止流程时发生错误: {exception.Message}",
                    5000);
            }
            finally
            {
                _allowClose = true;
                Close();
            }
        }

        private void MenuNewGraph_Click(object sender, RoutedEventArgs e) => FlowEditor.NewGraph();

        private void MenuClearGraph_Click(object sender, RoutedEventArgs e) => FlowEditor.ClearGraph();

        private void MenuLoadGraph_Click(object sender, RoutedEventArgs e) => FlowEditor.LoadGraph();

        private void MenuSaveGraph_Click(object sender, RoutedEventArgs e) => FlowEditor.SaveGraph();

        private void MenuSaveGraphAs_Click(object sender, RoutedEventArgs e) => FlowEditor.SaveGraphAs();

        private void MenuValidateGraph_Click(object sender, RoutedEventArgs e) => FlowEditor.ValidateGraph();

        private async void MenuRunOnce_Click(object sender, RoutedEventArgs e) => await FlowEditor.RunOnceAsync();

        private async void MenuRunContinuous_Click(object sender, RoutedEventArgs e) => await FlowEditor.RunContinuouslyAsync();

        private async void MenuStop_Click(object sender, RoutedEventArgs e) => await FlowEditor.StopExecutionAsync();

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

        private void UpdateExecutionCommandState()
        {
            if (MenuNewGraph == null)
            {
                return;
            }

            var idle = !FlowEditor.IsExecutionActive;
            MenuNewGraph.IsEnabled = idle;
            MenuClearGraph.IsEnabled = idle;
            MenuLoadGraph.IsEnabled = idle;
            MenuSaveGraph.IsEnabled = idle;
            MenuSaveGraphAs.IsEnabled = idle;
            MenuValidateGraph.IsEnabled = idle;
            MenuRunOnce.IsEnabled = idle;
            MenuRunContinuous.IsEnabled = idle;
            MenuStop.IsEnabled = !idle;
        }
    }
}
