```

BenchmarkDotNet v0.15.2, Windows 11 (10.0.26300.8935)
12th Gen Intel Core i7-12700 2.10GHz, 1 CPU, 20 logical and 12 physical cores
.NET SDK 11.0.100-rc.2.26504.105
  [Host]     : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX2 [AttachedDebugger]
  Job-WNYEYB : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX2

Runtime=.NET 10.0  IterationCount=10  WarmupCount=3  

```
| Method                         | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------------------- |---------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| &#39;ASP.NET MapGet + RDG — /ping&#39; | 46.46 μs | 23.36 μs | 15.45 μs |  1.10 |    0.49 | 0.2441 |  12.42 KB |        1.00 |
| &#39;KetPsi RD path — /ping&#39;       | 44.61 μs | 24.77 μs | 16.38 μs |  1.06 |    0.50 | 0.2441 |  12.73 KB |        1.02 |
