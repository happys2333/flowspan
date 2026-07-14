using Avalonia.Headless;
using Flowspan.Desktop;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: AvaloniaTestApplication(typeof(App))]
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerTest)]
