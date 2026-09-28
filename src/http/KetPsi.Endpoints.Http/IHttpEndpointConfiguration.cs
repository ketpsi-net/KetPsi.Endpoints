using System.Linq.Expressions;

namespace KetPsi.Endpoints.Http
{
    public interface IHttpEndpointConfiguration<in TEndpoint>
    {
        void Configure(IHttpEndpointBuilder<TEndpoint> builder);
    }

    public interface IHttpEndpointBuilder<out TEndpoint>
    {
    }

    public interface IHttpParameterBuilder<TParam>
    {
        public IHttpParameterBuilder<TParam> FromRoute(string? name = null) => this;
        public IHttpParameterBuilder<TParam> FromQuery(string? name = null) => this;
        public IHttpParameterBuilder<TParam> FromHeader(string? name = null) => this;
        public IHttpParameterBuilder<TParam> FromBody() => this;
        public IHttpParameterBuilder<TParam> FromForm(string? name = null) => this;
        public IHttpParameterBuilder<TParam> FromServices() => this;
        public IHttpParameterBuilder<TParam> FromKeyedServices(object key) => this;
        public IHttpAsParametersBuilder<TParam> AsParameters() => default!;
    }

    public interface IHttpAsParametersBuilder<TParam>
    {
        public IHttpAsParametersBuilder<TParam> Property<TProp>(
            Expression<Func<TParam, TProp>> propertySelector,
            Action<IHttpParameterBuilder<TProp>> configure) => this;
    }
}
