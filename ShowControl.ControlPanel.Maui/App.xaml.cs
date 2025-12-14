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
            _window.Add(window);
            return window;
        }
    }
}
