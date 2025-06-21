```

BenchmarkDotNet v0.14.0, Ubuntu 22.04.5 LTS (Jammy Jellyfish) WSL
Intel Core Ultra 7 165H, 1 CPU, 22 logical and 11 physical cores
.NET SDK 9.0.203
  [Host]     : .NET 9.0.4 (9.0.425.16305), X64 RyuJIT AVX2
  DefaultJob : .NET 9.0.4 (9.0.425.16305), X64 RyuJIT AVX2
  Dry        : .NET 9.0.4 (9.0.425.16305), X64 RyuJIT AVX2


```
| Method               | Job        | InvocationCount | IterationCount | LaunchCount | RunStrategy | UnrollFactor | WarmupCount | MatrixSize | Mean         | Error     | StdDev    | Median       | Gen0     | Gen1     | Gen2     | Allocated  |
|--------------------- |----------- |---------------- |--------------- |------------ |------------ |------------- |------------ |----------- |-------------:|----------:|----------:|-------------:|---------:|---------:|---------:|-----------:|
| QuantizedOperations  | DefaultJob | Default         | Default        | Default     | Default     | 16           | Default     | 128        |     227.3 μs |   6.30 μs |  18.28 μs |     222.8 μs |   6.3477 |        - |        - |   80.34 KB |
| BF16Operations       | DefaultJob | Default         | Default        | Default     | Default     | 16           | Default     | 128        |     254.6 μs |   9.88 μs |  28.67 μs |     245.6 μs |   7.8125 |   0.4883 |        - |   96.34 KB |
| FP16ToFP32Conversion | DefaultJob | Default         | Default        | Default     | Default     | 16           | Default     | 256        |   1,177.1 μs |  97.78 μs | 288.32 μs |   1,163.2 μs |        - |        - |        - |    3.72 KB |
| QuantizedOperations  | DefaultJob | Default         | Default        | Default     | Default     | 16           | Default     | 256        |   1,331.0 μs |  37.23 μs | 106.22 μs |   1,314.1 μs |  82.0313 |  82.0313 |  82.0313 |  320.42 KB |
| FP16ToFP32Conversion | DefaultJob | Default         | Default        | Default     | Default     | 16           | Default     | 512        |   1,500.0 μs |  69.53 μs | 205.00 μs |   1,502.5 μs |        - |        - |        - |    3.72 KB |
| BF16Operations       | DefaultJob | Default         | Default        | Default     | Default     | 16           | Default     | 256        |   1,513.5 μs |  97.61 μs | 287.81 μs |   1,580.3 μs | 123.0469 | 123.0469 | 123.0469 |  384.47 KB |
| FP16ToFP32Conversion | DefaultJob | Default         | Default        | Default     | Default     | 16           | Default     | 128        |   1,576.7 μs | 121.01 μs | 354.89 μs |   1,502.7 μs |        - |        - |        - |    3.73 KB |
| MixedPrecisionGEMM   | DefaultJob | Default         | Default        | Default     | Default     | 16           | Default     | 128        |   1,590.2 μs | 105.89 μs | 298.67 μs |   1,546.3 μs |        - |        - |        - |    5.05 KB |
| MixedPrecisionGEMM   | DefaultJob | Default         | Default        | Default     | Default     | 16           | Default     | 256        |   1,708.9 μs |  81.06 μs | 239.00 μs |   1,678.1 μs |        - |        - |        - |    5.06 KB |
| QuantizedOperations  | Dry        | 1               | 1              | 1           | ColdStart   | 1            | 1           | 128        |   2,346.8 μs |        NA |   0.00 μs |   2,346.8 μs |        - |        - |        - |   81.39 KB |
| QuantizedOperations  | Dry        | 1               | 1              | 1           | ColdStart   | 1            | 1           | 256        |   2,511.1 μs |        NA |   0.00 μs |   2,511.1 μs |        - |        - |        - |  321.39 KB |
| BF16Operations       | Dry        | 1               | 1              | 1           | ColdStart   | 1            | 1           | 128        |   2,579.3 μs |        NA |   0.00 μs |   2,579.3 μs |        - |        - |        - |   97.39 KB |
| QuantizedOperations  | Dry        | 1               | 1              | 1           | ColdStart   | 1            | 1           | 512        |   4,134.7 μs |        NA |   0.00 μs |   4,134.7 μs |        - |        - |        - | 1281.39 KB |
| BF16Operations       | Dry        | 1               | 1              | 1           | ColdStart   | 1            | 1           | 256        |   5,259.4 μs |        NA |   0.00 μs |   5,259.4 μs |        - |        - |        - |  385.39 KB |
| QuantizedOperations  | DefaultJob | Default         | Default        | Default     | Default     | 16           | Default     | 512        |   5,298.6 μs | 173.83 μs | 493.12 μs |   5,222.6 μs | 328.1250 | 328.1250 | 328.1250 | 1280.67 KB |
| BF16Operations       | DefaultJob | Default         | Default        | Default     | Default     | 16           | Default     | 512        |   6,598.6 μs | 220.66 μs | 643.68 μs |   6,608.3 μs | 492.1875 | 492.1875 | 492.1875 | 1536.84 KB |
| MixedPrecisionGEMM   | DefaultJob | Default         | Default        | Default     | Default     | 16           | Default     | 512        |   7,199.9 μs | 139.13 μs | 170.87 μs |   7,178.8 μs |        - |        - |        - |    5.07 KB |
| BF16Operations       | Dry        | 1               | 1              | 1           | ColdStart   | 1            | 1           | 512        |   7,219.1 μs |        NA |   0.00 μs |   7,219.1 μs |        - |        - |        - | 1537.39 KB |
| FP16ToFP32Conversion | Dry        | 1               | 1              | 1           | ColdStart   | 1            | 1           | 512        | 302,575.3 μs |        NA |   0.00 μs | 302,575.3 μs |        - |        - |        - |    6.59 KB |
| FP16ToFP32Conversion | Dry        | 1               | 1              | 1           | ColdStart   | 1            | 1           | 128        | 359,436.5 μs |        NA |   0.00 μs | 359,436.5 μs |        - |        - |        - |    5.65 KB |
| MixedPrecisionGEMM   | Dry        | 1               | 1              | 1           | ColdStart   | 1            | 1           | 512        | 382,959.2 μs |        NA |   0.00 μs | 382,959.2 μs |        - |        - |        - |    7.16 KB |
| FP16ToFP32Conversion | Dry        | 1               | 1              | 1           | ColdStart   | 1            | 1           | 256        | 396,166.6 μs |        NA |   0.00 μs | 396,166.6 μs |        - |        - |        - |    6.59 KB |
| MixedPrecisionGEMM   | Dry        | 1               | 1              | 1           | ColdStart   | 1            | 1           | 128        | 420,493.0 μs |        NA |   0.00 μs | 420,493.0 μs |        - |        - |        - |    8.43 KB |
| MixedPrecisionGEMM   | Dry        | 1               | 1              | 1           | ColdStart   | 1            | 1           | 256        | 433,986.4 μs |        NA |   0.00 μs | 433,986.4 μs |        - |        - |        - |    7.16 KB |
