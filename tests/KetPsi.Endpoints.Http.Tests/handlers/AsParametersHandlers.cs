using KetPsi.Endpoints.Abstractions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HttpEndpointGenerator.Tests.Handlers;

// =======================================================
// AsParameters – Record (positional / primary constructor)
// =======================================================
public record OrderFilter(
    string? Status,
    DateOnly? From,
    DateOnly? To,
    int? Page = 1,
    int? Size = 50);

public class GetOrdersWithRecordFilterHandler 
{
    public Task<IResult> ExecuteAsync(OrderFilter filter, CancellationToken ct)
    {
        return Task.FromResult(Results.Ok(new
        {
            filter.Status,
            filter.From,
            filter.To,
            filter.Page,
            filter.Size,
            Kind = "Record"
        }));
    }
}

// =======================================================
// AsParameters – Class with explicit constructor
// =======================================================
public class ProductSearchCriteria
{
    public ProductSearchCriteria(string? category, decimal? minPrice, decimal? maxPrice, bool? inStockOnly = false)
    {
        Category = category;
        MinPrice = minPrice;
        MaxPrice = maxPrice;
        InStockOnly = inStockOnly;
    }

    public string? Category { get; }
    public decimal? MinPrice { get; }
    public decimal? MaxPrice { get; }
    public bool? InStockOnly { get; } = false;
}

public class SearchProductsHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(ProductSearchCriteria criteria, CancellationToken ct)
    {
        return Task.FromResult(Results.Ok(new
        {
            criteria.Category,
            criteria.MinPrice,
            criteria.MaxPrice,
            criteria.InStockOnly,
            Kind = "ClassWithCtor"
        }));
    }
}

// =======================================================
// AsParameters – Property-only DTO (no constructor params)
// =======================================================
public class PaginationAndSort
{
    public int Page { get; set; } = 1;
    public int Size { get; set; } = 20;
    public string? SortBy { get; set; }
    public bool Descending { get; set; }
}

public class ListCustomersHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(PaginationAndSort paging, CancellationToken ct)
    {
        return Task.FromResult(Results.Ok(new
        {
            paging.Page,
            paging.Size,
            paging.SortBy,
            paging.Descending,
            Kind = "PropertyOnly"
        }));
    }
}

// =======================================================
// AsParameters – Mixed: record + additional simple params
// =======================================================
public record DateRange(DateOnly From, DateOnly To);

public class GetSalesReportHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(
        DateRange range,
        [FromQuery] string? region,
        [FromHeader(Name = "X-Report-Format")] string format = "json",
        CancellationToken ct = default)
    {
        return Task.FromResult(Results.Ok(new
        {
            range.From,
            range.To,
            Region = region,
            Format = format,
            Kind = "Mixed"
        }));
    }
}

// =======================================================
// AsParameters – Nested / complex with nullability
// =======================================================
public record CustomerFilter(
    string? Name,
    string? City, string? Country, string? PostalCode = null,
    bool? Active = true);

public class SearchCustomersComplexHandler 
{
    public Task<IResult> ExecuteAsync(CustomerFilter filter, CancellationToken ct)
    {
        return Task.FromResult(Results.Ok(new
        {
            filter.Name,
            City = filter.City,
            Country = filter.Country,
            Zip = filter.PostalCode,
            filter.Active,
            Kind = "FlattenedComplex"
        }));
    }
}

// =======================================================
// AsParameters – With default values and nullable value types
// =======================================================
public record InventoryQuery(
    string? Sku = null,
    int? WarehouseId = null,
    bool? IncludeReserved = false,
    decimal? MinQuantity = null);

public class GetInventoryHandler : IEndpoint
{
    public static Task<IResult> ExecuteAsync(InventoryQuery query, CancellationToken ct)
    {
        return Task.FromResult(Results.Ok(new
        {
            query.Sku,
            query.WarehouseId,
            query.IncludeReserved,
            query.MinQuantity,
            Kind = "DefaultsAndNullables"
        }));
    }
}
