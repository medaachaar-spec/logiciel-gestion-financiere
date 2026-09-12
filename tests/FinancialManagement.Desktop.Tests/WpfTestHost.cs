using System.Windows.Threading;

namespace FinancialManagement.Desktop.Tests;

internal static class WpfTestHost
{
    private static readonly Lazy<WpfDispatcherHost> Host = new(() => new WpfDispatcherHost());

    internal static void Invoke(Action action) => Host.Value.Invoke(action);

    private sealed class WpfDispatcherHost
    {
        private readonly ManualResetEventSlim _ready = new();
        private Dispatcher? _dispatcher;

        internal WpfDispatcherHost()
        {
            var thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "FinancialManagement WPF test host"
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(_ready.Wait(TimeSpan.FromSeconds(15)), "The WPF test host did not initialize.");
        }

        internal void Invoke(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            _dispatcher!.Invoke(action);
        }

        private void Run()
        {
            // Constructing a WPF Application queues its Startup callback. The
            // dispatcher below therefore requires an explicit resource-only mode;
            // otherwise App.OnStartup resolves and opens the production database.
            var app = new FinancialManagement.Desktop.App(runProductionStartup: false);
            app.InitializeComponent();
            _dispatcher = Dispatcher.CurrentDispatcher;
            _ready.Set();
            Dispatcher.Run();
        }
    }
}
