namespace ShowControl.ControlPanel.Maui
{
    public partial class App : Application
    {
        private List<Window> _window;
        public App()
        {
            _window = new List<Window>();
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = new Window(new AppShell());
            // Destroying is not cancellable on either platform, so this is a save-and-stop hook,
            // not a prompt. It is also what finally runs RenderService.StopAsync on a normal quit.
            window.Destroying += OnWindowDestroying;
            _window.Add(window);
            return window;
        }

        private void OnWindowDestroying(object? sender, EventArgs e)
        {
            if (sender is Window window)
            {
                window.Destroying -= OnWindowDestroying;
                _window.Remove(window);
            }

            // Only the last window going away means the app is closing; the preview window closing
            // must not stop the show.
            if (_window.Count == 0)
            {
                AppLifetime.Shutdown(TimeSpan.FromSeconds(2));
            }
        }
    }
}
