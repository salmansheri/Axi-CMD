using Xunit;

// The application uses process-wide Serilog and meters. Keep host lifetimes separate.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
