# ILGPU .NET 9 Compliance and Native AOT Compatibility Plan

## Executive Summary

This document outlines a comprehensive plan to modernize ILGPU for .NET 9 compliance and native AOT compatibility. The migration represents a **major architectural transformation** requiring fundamental changes to the code generation pipeline, native library integration, and reflection-based patterns.

**Timeline**: 12-18 months  
**Risk Level**: High (Core architecture changes)  
**Effort**: 3-5 senior developers  

## Current State Analysis

### Framework Status
- **Current**: .NET 8.0 primary, .NET 6.0/7.0 support
- **Language**: C# 11.0 (12.0 for .NET 8.0)
- **Dependencies**: Minimal external dependencies, heavy T4 template usage

### Critical AOT Blockers
1. **System.Reflection.Emit**: 27+ files, core to IL generation
2. **Dynamic Assembly Creation**: RuntimeSystem, Context, KernelLauncherBuilder
3. **Runtime Type Discovery**: Extensive typeof() and GetType() usage
4. **Activator.CreateInstance**: Dynamic object creation patterns

### Native Library Integration
- **CUDA APIs**: Comprehensive P/Invoke bindings via XML definitions
- **OpenCL APIs**: Platform-specific bindings with runtime discovery
- **Platform Detection**: Windows/Linux/macOS library loading

## Phase 1: .NET 9 Compliance (2-3 months)

### 1.1 Framework Target Update
**Files to modify:**
- `Src/Directory.Build.props` - Update LibraryTargetFrameworks
- `global.json` - Update SDK version to 9.0.x
- All `.csproj` files - Ensure compatibility

**Actions:**
```xml
<PropertyGroup>
  <LibraryTargetFrameworks>net9.0</LibraryTargetFrameworks>
  <LangVersion>13.0</LangVersion>
</PropertyGroup>
```

### 1.2 Dependency Modernization
- Update T4.Build to latest version
- Evaluate Microsoft.NETFramework.ReferenceAssemblies necessity
- Test all T4 template generations

### 1.3 Validation
- Build all projects targeting .NET 9.0
- Run full test suite across all backends
- Validate sample applications
- Update CI/CD pipeline (`.github/workflows/ci.yml`)

**Deliverables:**
- Fully functional ILGPU on .NET 9.0
- Updated CI/CD pipeline
- Migration guide for consumers

## Phase 2: AOT Foundation (4-6 months)

### 2.1 Source Generator Infrastructure
**New Components:**
- `ILGPU.SourceGenerators` project
- Kernel compilation source generators
- Type discovery source generators
- P/Invoke binding generators

**Architecture:**
```
ILGPU.SourceGenerators/
├── Generators/
│   ├── KernelCompilationGenerator.cs
│   ├── TypeDiscoveryGenerator.cs
│   └── NativeBindingGenerator.cs
├── Analysis/
│   ├── KernelMethodAnalyzer.cs
│   └── TypeAnalyzer.cs
└── Templates/
    ├── KernelLauncher.template
    └── NativeBinding.template
```

### 2.2 Reflection.Emit Replacement Strategy
**Current Pattern:**
```csharp
// Dynamic IL generation (AOT incompatible)
var method = new DynamicMethod("KernelLauncher", ...);
var il = method.GetILGenerator();
il.Emit(OpCodes.Ldarg_0);
```

**New Pattern:**
```csharp
// Source generator approach (AOT compatible)
[KernelLauncher(typeof(MyKernel))]
public static partial class MyKernelLauncher
{
    // Generated at compile time
    [GeneratedKernelLauncher]
    public static partial void Launch(AcceleratorStream stream, ...);
}
```

### 2.3 Compile-Time Kernel Analysis
- Implement Roslyn-based kernel method discovery
- Generate kernel metadata at compile time
- Create kernel dependency graphs
- Pre-compile backend-specific code

**Deliverables:**
- Source generator infrastructure
- Compile-time kernel discovery
- Initial Reflection.Emit replacement for core scenarios

## Phase 3: Native Library Modernization (3-4 months)

### 3.1 LibraryImport Migration
**Current (DllImport):**
```csharp
[DllImport("nvcuda", CallingConvention = CallingConvention.StdCall)]
public static extern CudaError cuInit(uint flags);
```

**Modernized (LibraryImport):**
```csharp
[LibraryImport("nvcuda")]
[UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
public static partial CudaError cuInit(uint flags);
```

### 3.2 AOT-Compatible Native Loading
**New Architecture:**
```
ILGPU.Native/
├── Loaders/
│   ├── CudaLibraryLoader.cs
│   ├── OpenCLLibraryLoader.cs
│   └── PlatformLibraryLoader.cs
├── Bindings/
│   ├── CUDA/
│   │   ├── CudaBindings.cs (Generated)
│   │   └── CudaBindings.xml (Source)
│   └── OpenCL/
│       ├── OpenCLBindings.cs (Generated)
│       └── OpenCLBindings.xml (Source)
└── AOT/
    ├── NativeAotSupport.cs
    └── TrimmerSupport.cs
```

### 3.3 CUDA Integration Enhancement
**Deep Integration Strategy:**
- Direct CUDA Driver API integration (bypass CUDA Runtime where possible)
- CUPTI integration for advanced profiling
- CUDA Graph API support for optimized execution
- Multi-GPU topology awareness

**New Features:**
```csharp
// Enhanced CUDA context management
public sealed class CudaContext
{
    public CudaGraphExec CreateGraph(Action<CudaGraphBuilder> builder);
    public CuptiProfiler CreateProfiler();
    public NvmlDevice GetDevice();
}

// Memory pool integration
public sealed class CudaMemoryPool
{
    public MemoryBuffer<T> AllocateAsync<T>(long length);
    public void SetThreshold(long bytes);
}
```

**Deliverables:**
- AOT-compatible native library loading
- Enhanced CUDA integration with modern APIs
- Trimmer-safe native bindings

## Phase 4: Complete AOT Transformation (4-6 months)

### 4.1 Runtime Elimination
**Components to Replace:**
- `RuntimeSystem` → `CompiledKernelSystem`
- `KernelLauncherBuilder` → `GeneratedKernelLaunchers`
- Dynamic type discovery → Source-generated type catalogs

### 4.2 Backend Modernization
**Velocity Backend:**
- Replace IL generation with compile-time SIMD code generation
- Use generic math interfaces (INumber<T>, IFloatingPoint<T>)
- Leverage .NET 9 SIMD improvements

**PTX Backend:**
- Pre-compile PTX templates
- Source-generate CUDA kernel wrappers
- Optimize for specific CUDA architectures

**OpenCL Backend:**
- Pre-compile OpenCL C kernels
- Source-generate platform-specific optimizations
- Integrate with OpenCL 3.0 features

### 4.3 Memory Management Optimization
**Modern Patterns:**
```csharp
// Span<T> and Memory<T> integration
public ref struct KernelMemoryView<T>
{
    public ReadOnlySpan<T> Input { get; }
    public Span<T> Output { get; }
}

// NativeMemory usage
public static class GpuMemoryManager
{
    public static unsafe void* AllocateAligned(nuint size, nuint alignment);
    public static void FreeAligned(unsafe void* ptr);
}
```

**Deliverables:**
- Fully AOT-compatible ILGPU
- Performance benchmarks vs. current implementation
- Migration tools for existing users

## Phase 5: Optimization and Enhancement (2-3 months)

### 5.1 Performance Optimizations
**Code Generation:**
- Aggressive inlining for kernel hot paths
- Template specialization for common scenarios
- Compile-time constant folding

**Memory Management:**
- Zero-copy buffer operations where possible
- Memory pool pre-warming
- NUMA-aware allocations

**Execution:**
- Async execution optimization
- Stream synchronization improvements
- Multi-GPU workload distribution

### 5.2 Developer Experience
**Tooling:**
- Visual Studio extensions for kernel debugging
- Performance profiler integration
- Kernel optimization analyzer

**Documentation:**
- Migration guide from reflection-based to AOT
- Performance optimization best practices
- Advanced CUDA integration examples

### 5.3 Modern .NET Features
**.NET 9 Specific:**
- Generic math interface adoption
- SearchValues<T> for optimized lookups
- System.Threading.Lock usage
- Improved SIMD support

**Deliverables:**
- Comprehensive performance improvements
- Enhanced developer experience
- Modern .NET feature integration

## Risk Mitigation

### Technical Risks
1. **Breaking Changes**: Implement feature flags for gradual migration
2. **Performance Regression**: Continuous benchmarking against current version
3. **Compatibility**: Maintain bridge layer for existing APIs

### Migration Strategy
```csharp
// Compatibility layer
#if LEGACY_REFLECTION
    // Current reflection-based implementation
#else
    // New AOT-compatible implementation
#endif
```

### Testing Strategy
- **Unit Tests**: 95%+ coverage maintained throughout migration
- **Integration Tests**: All GPU backends continuously tested
- **Performance Tests**: Regression detection and improvement tracking
- **Compatibility Tests**: Existing user code validation

## Success Metrics

### Performance Targets
- **Startup Time**: 10x improvement (AOT compilation eliminates JIT overhead)
- **Memory Usage**: 30% reduction (eliminate reflection metadata)
- **Kernel Launch**: 5% improvement (optimized code paths)

### Compatibility Targets
- **API Compatibility**: 95% source-level compatibility
- **Feature Parity**: 100% feature coverage in AOT mode
- **Platform Support**: All current platforms (Windows, Linux, macOS)

## Conclusion

This modernization plan represents a fundamental transformation of ILGPU from a reflection-heavy JIT compiler to a modern, AOT-compatible GPU computing framework. While the effort is substantial, the benefits include:

- **Better Performance**: Eliminated JIT overhead and optimized code paths
- **Smaller Deployments**: Trimmed, self-contained applications
- **Modern Architecture**: Leveraging latest .NET and GPU computing advances
- **Enhanced CUDA Integration**: Direct access to latest CUDA features

The phased approach ensures continuous functionality while systematically addressing AOT compatibility challenges. Success requires dedicated team effort and comprehensive testing throughout the migration process.