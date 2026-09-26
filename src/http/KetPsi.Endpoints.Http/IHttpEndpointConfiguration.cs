using System.Linq.Expressions;

namespace KetPsi.Endpoints.Http
{
    public interface IHttpEndpointConfiguration<TEndpoint>
    {
        void Configure(HttpEndpointBuilder<TEndpoint> builder);
    }

    public sealed class HttpEndpointBuilder<TEndpoint>
    {
        private HttpEndpointBuilder() { }
    }

    public sealed class HttpParameterBuilder<TParam>
    {
        private HttpParameterBuilder() { }

        public HttpParameterBuilder<TParam> FromRoute(string? name = null) => this;
        public HttpParameterBuilder<TParam> FromQuery(string? name = null) => this;
        public HttpParameterBuilder<TParam> FromHeader(string? name = null) => this;
        public HttpParameterBuilder<TParam> FromBody() => this;
        public HttpParameterBuilder<TParam> FromForm(string? name = null) => this;
        public HttpParameterBuilder<TParam> FromServices() => this;
        public HttpParameterBuilder<TParam> FromKeyedServices(object key) => this;
        public HttpAsParametersBuilder<TParam> AsParameters() => default!;
    }

    public sealed class HttpAsParametersBuilder<TParam>
    {
        private HttpAsParametersBuilder() { }

        public HttpAsParametersBuilder<TParam> Property<TProp>(
            Expression<Func<TParam, TProp>> propertySelector,
            Action<HttpParameterBuilder<TProp>> configure) => this;
    }
}
