using Microsoft.AspNetCore.Builder;

namespace KetPsi.Endpoints.Http.Internals
{
    internal sealed class EmptyEndpointConventionBuilder(IServiceProvider serviceProvider) : IEndpointConventionBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;

        /// <summary>
        /// This method does nothing but return 'this' to allow for fluent chaining.
        /// </summary>
        public void Add(Action<EndpointBuilder> convention)
        {
            // No-op
        }

        /// <summary>
        /// This method does nothing but return 'this' to allow for fluent chaining.
        /// </summary>
        public void Finally(Action<EndpointBuilder> finallyConvention)
        {
            // No-op
        }
    }
}
