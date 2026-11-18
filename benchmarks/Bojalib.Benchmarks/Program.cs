using BenchmarkDotNet.Running;

// Run all:      dotnet run -c Release --project benchmarks/Bojalib.Benchmarks
// Run a subset: dotnet run -c Release --project benchmarks/Bojalib.Benchmarks -- --filter "*Sort*"
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
