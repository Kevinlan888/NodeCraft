using CommonControls.WPF;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.Pages;
using NodeCraft.Plugins;
using NodeCraft.Theming;

namespace NodeCraft
{
    public partial class MainWindow : FramelessWindowEx
    {
        private readonly FlowPage FlowEditor;
        private readonly ApplicationThemeManager _themeManager;
        private readonly ThemePreferenceStore _themePreferenceStore;
        private bool _notificationServiceRegistered;
        private bool _pluginFailureNotificationShown;
        private bool _startupGraphLoaded;
        private bool _allowClose;
        private bool _closingInProgress;
        private bool _synchronizingTheme;

        public MainWindow(
            FlowPage flowPage,
            ApplicationThemeManager themeManager,
            ThemePreferenceStore themePreferenceStore)
        {
            FlowEditor = flowPage ?? throw new ArgumentNullException(nameof(flowPage));
            _themeManager = themeManager ?? throw new ArgumentNullException(nameof(themeManager));
            _themePreferenceStore = themePreferenceStore
                ?? throw new ArgumentNullException(nameof(themePreferenceStore));

            InitializeComponent();
            _synchronizingTheme = true;
            try
            {
                DarkThemeMenuItem.IsChecked = _themeManager.CurrentTheme
                    == CommonControlTheme.BaseTheme.Dark;
            }
            finally
            {
                _synchronizingTheme = false;
            }

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
            if (_allowClose)
            {
                return;
            }

            if (_closingInProgress)
            {
                e.Cancel = true;
                return;
            }

            if (!FlowEditor.IsExecutionActive)
            {
                if (!ConfirmSaveChanges())
                {
                    e.Cancel = true;
                    return;
                }

                _allowClose = true;
                return;
            }

            e.Cancel = true;
            _closingInProgress = true;
            try
            {
                if (FlowEditor.IsExecutionActive)
                {
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
                }

                if (!ConfirmSaveChanges())
                {
                    return;
                }

                _allowClose = true;
                _ = Dispatcher.BeginInvoke(new Action(Close));
            }
            finally
            {
                _closingInProgress = false;
            }
        }

        private void MenuNewGraph_Click(object sender, RoutedEventArgs e)
        {
            if (ConfirmSaveChanges())
            {
                FlowEditor.NewGraph();
            }
        }

        private void MenuClearGraph_Click(object sender, RoutedEventArgs e)
        {
            if (ConfirmSaveChanges())
            {
                FlowEditor.ClearGraph();
            }
        }

        private void MenuLoadGraph_Click(object sender, RoutedEventArgs e)
        {
            if (ConfirmSaveChanges())
            {
                FlowEditor.LoadGraph();
            }
        }

        private void MenuSaveGraph_Click(object sender, RoutedEventArgs e) => FlowEditor.SaveGraph();

        private void MenuSaveGraphAs_Click(object sender, RoutedEventArgs e) => FlowEditor.SaveGraphAs();

        private void MenuCloseGraph_Click(object sender, RoutedEventArgs e)
        {
            if (ConfirmSaveChanges())
            {
                FlowEditor.CloseGraph();
            }
        }

        private void MenuValidateGraph_Click(object sender, RoutedEventArgs e) => FlowEditor.ValidateGraph();

        private async void MenuRunOnce_Click(object sender, RoutedEventArgs e) => await FlowEditor.RunOnceAsync();

        private async void MenuRunContinuous_Click(object sender, RoutedEventArgs e) => await FlowEditor.RunContinuouslyAsync();

        private async void MenuStop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await FlowEditor.StopExecutionAsync();
            }
            catch (Exception exception)
            {
                NotificationService.ShowNotification(
                    "nodecraft-flow-stop",
                    $"停止流程时发生错误: {exception.Message}",
                    5000);
            }
        }

        private void MenuExit_Click(object sender, RoutedEventArgs e) => Close();

        private bool ConfirmSaveChanges()
        {
            if (!FlowEditor.HasUnsavedChanges)
            {
                return true;
            }

            var result = MessageBox.Show(
                this,
                "当前方案有未保存的修改，是否保存？",
                "保存方案",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                return FlowEditor.SaveGraph();
            }

            return result == MessageBoxResult.No;
        }

        private void DarkThemeMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            if (!_synchronizingTheme)
                ChangeTheme(CommonControlTheme.BaseTheme.Dark);
        }

        private void DarkThemeMenuItem_Unchecked(object sender, RoutedEventArgs e)
        {
            if (!_synchronizingTheme)
                ChangeTheme(CommonControlTheme.BaseTheme.Light);
        }

        private void ChangeTheme(CommonControlTheme.BaseTheme theme)
        {
            _themeManager.Apply(theme);
            _themePreferenceStore.Save(theme);
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
            MenuCloseGraph.IsEnabled = idle;
            MenuValidateGraph.IsEnabled = idle;
            MenuRunOnce.IsEnabled = idle;
            MenuRunContinuous.IsEnabled = idle;
            MenuStop.IsEnabled = !idle;
        }
    }
}
