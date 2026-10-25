```

BenchmarkDotNet v0.15.2, Windows 11 (10.0.26300.8935)
12th Gen Intel Core i7-12700 2.10GHz, 1 CPU, 20 logical and 12 physical cores
.NET SDK 11.0.100-rc.2.26504.105
  [Host]     : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX2 [AttachedDebugger]
  Job-WNYEYB : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX2

Runtime=.NET 10.0  IterationCount=10  WarmupCount=3  

```
| Method                            | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------------------------- |---------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| &#39;ASP.NET MapGet + RDG — /echo?q=&#39; | 42.00 μs | 19.27 μs | 11.47 μs |  1.06 |    0.36 | 0.2441 |  12.93 KB |        1.00 |
| &#39;KetPsi RD path — /echo?q=&#39;       | 42.88 μs | 22.69 μs | 13.50 μs |  1.08 |    0.41 | 0.2441 |   13.2 KB |        1.02 |
