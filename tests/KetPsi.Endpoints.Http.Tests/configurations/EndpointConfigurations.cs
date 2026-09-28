using HttpEndpointGenerator.Tests.Handlers;

using KetPsi.Endpoints.Abstractions;
using KetPsi.Endpoints.Http;

namespace HttpEndpointGenerator.Tests.Configurations;

// -------------------------------------------------------
// External config for OrderFilter (record) – AsParameters
// -------------------------------------------------------
public class GetOrdersWithRecordFilterConfig : IHttpEndpointConfiguration<GetOrdersWithRecordFilterHandler>
{
    public void Configure(IHttpEndpointBuilder<GetOrdersWithRecordFilterHandler> builder)
    {
        builder.Filter()
               .AsParameters()
               .Property(x => x.Status, p => p.FromQuery())
               .Property(x => x.From, p => p.FromQuery(name: "fromDate"))
               .Property(x => x.To, p => p.FromQuery(name: "toDate"))
               .Property(x => x.Page, p => p.FromQuery())
               .Property(x => x.Size, p => p.FromQuery());
    }
}

// -------------------------------------------------------
// External config for ProductSearchCriteria (class + ctor)
// -------------------------------------------------------
public class SearchProductsConfig : IHttpEndpointConfiguration<SearchProductsHandler>
{
    public void Configure(IHttpEndpointBuilder<SearchProductsHandler> builder)
    {
        builder.Criteria()
               .AsParameters()
               .Property(x => x.Category, p => p.FromQuery())
               .Property(x => x.MinPrice, p => p.FromQuery(name: "min"))
               .Property(x => x.MaxPrice, p => p.FromQuery(name: "max"))
               .Property(x => x.InStockOnly, p => p.FromQuery(name: "inStock"));
    }
}

// -------------------------------------------------------
// External config for property-only DTO
// -------------------------------------------------------
public class ListCustomersConfig : IHttpEndpointConfiguration<ListCustomersHandler>
{
    public void Configure(IHttpEndpointBuilder<ListCustomersHandler> builder)
    {
        builder.Paging()
               .AsParameters()
               .Property(x => x.Page, p => p.FromQuery())
               .Property(x => x.Size, p => p.FromQuery())
               .Property(x => x.SortBy, p => p.FromQuery(name: "sort"))
               .Property(x => x.Descending, p => p.FromQuery(name: "desc"));
    }
}

// -------------------------------------------------------
// External config for mixed case (DateRange + extra params)
// -------------------------------------------------------
public class GetSalesReportConfig : IHttpEndpointConfiguration<GetSalesReportHandler>
{
    public void Configure(IHttpEndpointBuilder<GetSalesReportHandler> builder)
    {
        builder.Range()
               .AsParameters()
               .Property(x => x.From, p => p.FromQuery(name: "from"))
               .Property(x => x.To, p => p.FromQuery(name: "to"));

        // The other parameters (region, format) stay with their inline attributes
    }
}

// -------------------------------------------------------
// External config for flattened complex filter
// -------------------------------------------------------
public class SearchCustomersComplexConfig : IHttpEndpointConfiguration<SearchCustomersComplexHandler>
{
    public void Configure(IHttpEndpointBuilder<SearchCustomersComplexHandler> builder)
    {
        builder.Filter()
               .AsParameters()
               .Property(x => x.Name, p => p.FromQuery())
               .Property(x => x.City, p => p.FromQuery())
               .Property(x => x.Country, p => p.FromQuery())
               .Property(x => x.PostalCode, p => p.FromQuery(name: "zip"))
               .Property(x => x.Active, p => p.FromQuery());
    }
}

// -------------------------------------------------------
// External config for inventory (defaults + nullables)
// -------------------------------------------------------
public class GetInventoryConfig : IHttpEndpointConfiguration<GetInventoryHandler>
{
    public void Configure(IHttpEndpointBuilder<GetInventoryHandler> builder)
    {
        builder.Query()
               .AsParameters()
               .Property(x => x.Sku, p => p.FromQuery())
               .Property(x => x.WarehouseId, p => p.FromQuery(name: "wh"))
               .Property(x => x.IncludeReserved, p => p.FromQuery(name: "reserved"))
               .Property(x => x.MinQuantity, p => p.FromQuery(name: "minQty"));
    }
}

// -------------------------------------------------------
// Edge-case AsParameters configs
// -------------------------------------------------------
public class SearchByTermConfig : IHttpEndpointConfiguration<SearchByTermHandler>
{
    public void Configure(IHttpEndpointBuilder<SearchByTermHandler> builder)
    {
        builder.Filter()
               .AsParameters()
               .Property(x => x.Term, p => p.FromQuery(name: "q"));
    }
}

public class SearchInitOnlyConfig : IHttpEndpointConfiguration<SearchInitOnlyHandler>
{
    public void Configure(IHttpEndpointBuilder<SearchInitOnlyHandler> builder)
    {
        builder.Criteria()
               .AsParameters()
               .Property(x => x.Category, p => p.FromQuery())
               .Property(x => x.MinScore, p => p.FromQuery(name: "min"));
    }
}

public class LookupByCodeConfig : IHttpEndpointConfiguration<LookupByCodeHandler>
{
    public void Configure(IHttpEndpointBuilder<LookupByCodeHandler> builder)
    {
        builder.Filter()
               .AsParameters()
               .Property(x => x.Code, p => p.FromQuery())
               .Property(x => x.OptionalNote, p => p.FromQuery(name: "note"));
    }
}

public class EmptyishConfig : IHttpEndpointConfiguration<EmptyishHandler>
{
    public void Configure(IHttpEndpointBuilder<EmptyishHandler> builder)
    {
        builder.Filter()
               .AsParameters()
               .Property(x => x.A, p => p.FromQuery())
               .Property(x => x.B, p => p.FromQuery());
    }
}

public class CollisionConfig : IHttpEndpointConfiguration<CollisionHandler>
{
    public void Configure(IHttpEndpointBuilder<CollisionHandler> builder)
    {
        // External config + inline [FromQuery] on the parameter → collision case
        builder.Filter()
               .AsParameters()
               .Property(x => x.Q, p => p.FromQuery(name: "query"))
               .Property(x => x.Page, p => p.FromQuery());
    }
}
