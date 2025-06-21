```

BenchmarkDotNet v0.14.0, Ubuntu 22.04.5 LTS (Jammy Jellyfish) WSL
Intel Core Ultra 7 165H, 1 CPU, 22 logical and 11 physical cores
.NET SDK 9.0.203
  [Host] : .NET 9.0.4 (9.0.425.16305), X64 RyuJIT AVX2


```
| Method            | TensorSize | Mean | Error | Ratio | RatioSD | Alloc Ratio |
|------------------ |----------- |-----:|------:|------:|--------:|------------:|
| CpuOnlyProcessing | 64         |   NA |    NA |     ? |       ? |           ? |

Benchmarks with issues:
  HybridProcessingBenchmarks.CpuOnlyProcessing: DefaultJob [TensorSize=64]
