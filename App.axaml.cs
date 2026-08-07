using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MusicVideoMediaPlayer.ViewModels;
using MusicVideoMediaPlayer.Views;

namespace MusicVideoMediaPlayer;

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
            desktop.Exit += (_, _) => vm.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}