```

BenchmarkDotNet v0.15.2, Windows 11 (10.0.26300.8935)
12th Gen Intel Core i7-12700 2.10GHz, 1 CPU, 20 logical and 12 physical cores
.NET SDK 11.0.100-rc.2.26504.105
  [Host]     : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX2 [AttachedDebugger]
  Job-WNYEYB : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX2

Runtime=.NET 10.0  IterationCount=10  WarmupCount=3  

```
| Method                               | Mean     | Error    | StdDev   | Median   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------------------------- |---------:|---------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| &#39;ASP.NET MapGet + RDG — /items/{id}&#39; | 63.58 μs | 32.42 μs | 21.44 μs | 50.48 μs |  1.10 |    0.48 | 0.2441 |  12.77 KB |        1.00 |
| &#39;KetPsi RD path — /items/{id}&#39;       | 47.75 μs | 33.78 μs | 22.35 μs | 35.79 μs |  0.83 |    0.45 | 0.2441 |  13.01 KB |        1.02 |
