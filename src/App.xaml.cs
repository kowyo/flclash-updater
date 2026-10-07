using Microsoft.UI.Xaml;
namespace FlClashUpdater;
public partial class App : Application {
    readonly string[] args;
    Window window;
    public App(string[] args) { this.args = args; InitializeComponent(); UnhandledException += (_, e) => { Engine.Log(e.Exception.ToString()); }; }
    protected override void OnLaunched(LaunchActivatedEventArgs args) {
        window = new MainWindow(this.args);
        window.Activate();
    }
}
