﻿using Hardcodet.Wpf.TaskbarNotification;
using MyPad.ViewModels;
using MyPad.ViewModels.Events;
using Plow.Wpf;
using Prism.Events;
using Prism.Ioc;
using Prism.Regions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Unity;
using Vanara.PInvoke;

namespace MyPad.Views
{
    public partial class Workspace : Window
    {
        #region インジェクション

        // Constructor Injection
        public IEventAggregator EventAggregator { get; set; }

        // Dependency Injection
        [Dependency]
        public IContainerExtension ContainerExtension { get; set; }
        [Dependency]
        public IRegionManager RegionManager { get; set; }

        #endregion

        #region プロパティ

        private HwndSource _handleSource;

        #endregion

        [InjectionConstructor]
        public Workspace(IEventAggregator eventAggregator)
        {
            this.InitializeComponent();
            this.EventAggregator = eventAggregator;

            void createWindow() => this.CreateWindow().Show();
            this.EventAggregator.GetEvent<CreateWindowEvent>().Subscribe(createWindow);
            void showBalloon((string title, string message) args) => this.TaskbarIcon.ShowBalloonTip(args.title, args.message, BalloonIcon.Info);
            this.EventAggregator.GetEvent<RaiseBalloonEvent>().Subscribe(showBalloon);
        }

        private MainWindow CreateWindow()
            => this.ContainerExtension.Resolve<MainWindow>((typeof(IRegionManager), this.RegionManager.CreateRegionManager()));

        private IEnumerable<MainWindow> GetWindows()
            => Application.Current?.Windows.OfType<MainWindow>() ?? Enumerable.Empty<MainWindow>();

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            this._handleSource = (HwndSource)PresentationSource.FromVisual(this);
            this._handleSource.AddHook(this.WndProc);
            this.Hide();

            var args = ((App)Application.Current).CommandLineArgs;
            var view = this.ContainerExtension.Resolve<MainWindow>();
            if (args.Any())
                view.ViewModel.LoadCommand.Execute(args);
            view.Show();
            if (view.ViewModel.SettingsService.IsDifferentVersion())
                view.ViewModel.DialogService.ToastNotify(string.Format(Properties.Resources.Message_NotifyWelcome, view.ViewModel.ProductInfo.Product, view.ViewModel.ProductInfo.Version));
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            this._handleSource.RemoveHook(this.WndProc);
            this.Descendants().OfType<TaskbarIcon>().ForEach(t => t.Dispose());
        }

        private void Window_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            void viewModel_Disposed(object sender, EventArgs e)
            {
                ((ViewModelBase)sender).Disposed -= viewModel_Disposed;
                this.Dispatcher.InvokeAsync(() => this.Close());
            }

            if (e.OldValue is ViewModelBase oldViewModel)
                oldViewModel.Disposed -= viewModel_Disposed;
            if (e.NewValue is ViewModelBase newViewModel)
                newViewModel.Disposed += viewModel_Disposed;
        }

        private void TaskbarIcon_TrayContextMenuOpen(object sender, RoutedEventArgs e)
        {
            this.WindowListItem.Items.Clear();
            var windows = this.GetWindows();
            for (var i = 0; i < windows.Count(); i++)
                this.WindowListItem.Items.Add(new MenuItem { DataContext = windows.ElementAt(i) });
        }

        private void TaskbarIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
        {
            // ウィンドウが存在する場合はそれらをフォアグラウンドに移動する
            // ウィンドウが一つも存在しない場合は新しいウィンドウを生成する
            var windows = this.GetWindows();
            if (windows.Any())
                windows.ForEach(w => w.SetForegroundWindow());
            else
                this.CreateWindow().Show();
        }

        private void NewWindowCommand_Click(object sender, RoutedEventArgs e)
        {
            this.CreateWindow().Show();
        }

        private void WindowItem_Click(object sender, RoutedEventArgs e)
        {
            ((sender as FrameworkElement)?.DataContext as Window)?.SetForegroundWindow();
        }

        private IntPtr WndProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch ((User32.WindowMessage)msg)
            {
                case User32.WindowMessage.WM_COPYDATA:
                {
                    var structure = Marshal.PtrToStructure<App.COPYDATASTRUCT>(lParam);
                    if (string.IsNullOrEmpty(structure.lpData) == false)
                    {
                        var paths = structure.lpData.Split('\t');
                        var window = Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();
                        if (window == null)
                        {
                            window = this.CreateWindow();
                            window.Show();
                        }
                        (window.DataContext as MainWindowViewModel)?.LoadCommand.Execute(paths);
                        window.SetForegroundWindow();
                    }
                    else
                    {
                        var windows = this.GetWindows();
                        if (windows.Any())
                            (windows.FirstOrDefault(w => w.IsActive) ?? windows.First()).SetForegroundWindow();
                        else
                            this.CreateWindow().Show();
                    }
                    break;
                }
            }
            return IntPtr.Zero;
        }
    }
}
