# ILGPU Benchmarks

Comprehensive benchmark suite for ILGPU Phase 6 implementations including Tensor Core integration, SIMD unification, and performance profiling.

## Features Benchmarked

### Tensor Core Operations
- Matrix Multiply-Accumulate (MMA) operations
- Mixed precision computations (FP16, BF16, TF32, INT8)
- Tensor fragment operations
- Direct PTX tensor core bindings

### SIMD Vector Operations
- Platform-specific intrinsics (AVX, SSE, NEON)
- System.Numerics.Vector operations
- Cross-platform vectorization
- Matrix-vector operations

### Hybrid Processing
- CPU/GPU workload distribution
- Adaptive threshold strategies
- Pipeline processing patterns
- Zero-copy memory operations

### Memory Operations
- Unified memory performance
- Memory layout optimization
- Memory pooling and allocation
- Cache efficiency patterns

## Running Benchmarks

### Interactive Mode (Default)
```bash
cd Src/ILGPU.Benchmarks
dotnet run --configuration Release
```

### Unattended Mode (CI/CD Integration)
```bash
# Run all benchmarks unattended with GitHub-ready output
dotnet run --configuration Release -- --unattended

# Short form
dotnet run --configuration Release -- -u
```

The unattended mode generates multiple output formats:
- **README_Benchmarks.md** - GitHub README-ready markdown with performance summary
- **benchmark_results.json** - Structured JSON data for programmatic consumption  
- **benchmark_results.csv** - CSV export for data analysis and visualization
- **comprehensive_report.md** - Detailed technical report with system information

### Available Benchmark Suites

1. **Quick Performance Suite** - Fast validation benchmarks
2. **Tensor Core Benchmarks** - Comprehensive tensor operations
3. **SIMD Benchmarks** - Vector processing performance
4. **Hybrid Processing Benchmarks** - CPU/GPU coordination
5. **Memory Benchmarks** - Memory operation optimization
6. **Comprehensive Suite** - All benchmarks (may take hours)
7. **Burn-in Test** - Maximum load testing

### Configuration

Benchmark behavior can be configured via `appsettings.json`:

- **Tensor Core Settings**: Compute capability requirements, precision preferences
- **SIMD Settings**: Platform detection, vector sizes, fallback behavior
- **Memory Settings**: Unified memory, buffer sizes, pooling
- **Burn-in Settings**: Duration, monitoring, safety thresholds

### Output

Benchmark results are saved in multiple formats:
- HTML reports for detailed analysis
- CSV exports for data processing
- Markdown summaries for documentation
- JSON exports for programmatic access

## Benchmark Classes

### Core Benchmarks
- `TensorCoreBenchmarks` - Tensor core operations and MMA
- `SimdVectorBenchmarks` - SIMD vector operations
- `MixedPrecisionBenchmarks` - Multi-precision arithmetic
- `BFloat16Benchmarks` - Brain floating point operations

### Platform and Optimization
- `PlatformIntrinsicsBenchmarks` - AVX/SSE/NEON intrinsics
- `MatrixVectorBenchmarks` - Linear algebra operations
- `CpuGpuComparisonBenchmarks` - Cross-platform performance

### Advanced Processing
- `HybridProcessingBenchmarks` - Heterogeneous computing
- `PipelineBenchmarks` - Async processing patterns
- `MemoryBenchmarks` - Memory operation optimization
- `MemoryLayoutBenchmarks` - Data layout strategies
- `UnifiedMemoryBenchmarks` - Zero-copy operations
- `ScalabilityBenchmarks` - Performance scaling analysis

### Stress Testing
- `BurnInTestRunner` - Maximum load and throughput testing

## Hardware Requirements

### Minimum Requirements
- .NET 8.0 Runtime
- 8GB RAM
- Multi-core CPU with SIMD support

### Recommended for Full Testing
- NVIDIA GPU with Compute Capability 7.0+ (for Tensor Cores)
- 16GB+ RAM
- CUDA 11.0+ drivers
- OpenCL 1.2+ drivers

### Platform Support
- **Windows**: Full support (CUDA, AVX, SSE)
- **Linux**: Full support (CUDA, AVX, SSE)
- **macOS**: CPU-only (NEON on ARM64)

## Understanding Results

### Key Metrics
- **Throughput**: Operations per second
- **Latency**: Time per operation
- **Memory Bandwidth**: Data transfer rates
- **Efficiency**: Compute utilization

### Performance Baselines
- Scalar operations provide baseline performance
- SIMD operations show vectorization benefits
- GPU operations demonstrate acceleration potential
- Hybrid strategies optimize workload distribution

### Interpreting Output
- **Ratio columns**: Performance relative to baseline
- **Memory columns**: Allocation and GC pressure
- **Error margins**: Statistical confidence intervals
- **Scaling metrics**: Performance vs problem size

## Troubleshooting

### Common Issues
- **CUDA not available**: Benchmarks fall back to CPU
- **Out of memory**: Reduce problem sizes in configuration
- **Driver issues**: Ensure latest GPU drivers installed
- **Platform limitations**: Some features may not be available

### Performance Tips
- Close other applications during benchmarking
- Ensure adequate cooling for sustained workloads
- Use Release configuration for accurate measurements
- Run multiple iterations for statistical significance

## CI/CD Integration

### GitHub Actions Example
```yaml
name: Performance Benchmarks

on:
  push:
    branches: [ main ]
  pull_request:
    branches: [ main ]

jobs:
  benchmark:
    runs-on: ubuntu-latest
    
    steps:
    - uses: actions/checkout@v4
    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '9.0.x'
        
    - name: Run Benchmarks
      run: |
        cd Src/ILGPU.Benchmarks
        dotnet run --configuration Release -- --unattended
        
    - name: Upload Results
      uses: actions/upload-artifact@v4
      with:
        name: benchmark-results
        path: |
          Src/ILGPU.Benchmarks/BenchmarkResults/*.md
          Src/ILGPU.Benchmarks/BenchmarkResults/*.json
          Src/ILGPU.Benchmarks/BenchmarkResults/*.csv
        
    - name: Update README
      run: |
        # Copy benchmark results to project README
        cat Src/ILGPU.Benchmarks/BenchmarkResults/README_Benchmarks.md >> README.md
        git add README.md
        git commit -m "Update benchmark results [skip ci]" || exit 0
        git push
```

### Performance Tracking
The benchmark suite automatically:
- Detects system configuration and capabilities
- Measures performance across different hardware configurations
- Generates trend-analysis friendly data formats
- Provides regression detection through baseline comparisons

## Integration with ILGPU Development

This benchmark suite is designed to:
- Validate Phase 6 implementation performance
- Identify optimization opportunities
- Regression test performance changes
- Guide hardware-specific optimizations
- Support continuous performance monitoring
- Generate publication-ready performance data

Results can be integrated into CI/CD pipelines for automated performance tracking and regression detection. The output formats are suitable for:
- **Academic Papers:** Comprehensive technical reports with methodology
- **Blog Posts:** GitHub-ready markdown with visual performance summaries  
- **Documentation:** Structured JSON/CSV data for automated documentation
- **Presentations:** Performance insights and key findings sections