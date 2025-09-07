using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SignalRServer
{
    public class Startup
    {
        private readonly IConfiguration _config;

        public Startup(IConfiguration config)
        {
            _config = config;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSignalR(options =>
            {
                options.MaximumReceiveMessageSize = 10 * 1024 * 1024; // 10MB
            })
            .AddMessagePackProtocol();  
        }

        public void Configure(IApplicationBuilder app)
        {
            var hubPath = _config["hubPath"] ?? "/dataHub"; // ±âº»°ª /dataHub

            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapHub<MsgHub>(hubPath);
            });
        }
    }
} 