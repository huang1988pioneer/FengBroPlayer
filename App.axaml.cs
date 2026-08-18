using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FengBroPlayer33.ViewModels;
using FengBroPlayer33.Views;

namespace FengBroPlayer33;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainViewModel();
            desktop.MainWindow = new MainWindow
            {
                DataContext = vm,
            };
            desktop.Exit += (_, _) =>
            {
                Program.StartExitWatchdog();
                vm.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}