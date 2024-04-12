using DayRateServices = DayRateService.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddGrpc();//.AddServiceOptions(options => options.);
builder.Services.AddGrpc().AddJsonTranscoding();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("DayRateLoadBalancer"));

//builder.Services.AddHealthChecks();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseRouting();

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

// must be added after UseRouting and before UseEndpoints 
//we will set default to true, to enable defualt GRPC-WEB interface on each GRPC.addService
app.UseGrpcWeb(new GrpcWebOptions() { DefaultEnabled = true });

app.UseCors();
app.UseEndpoints(endpoints =>
{
    endpoints.MapGrpcService<DayRateServices.DayRateService>().EnableGrpcWeb();

    endpoints.MapGet("/availableGrpcRoutes", async context =>
    {
        var endpointDataSource = context
            .RequestServices.GetRequiredService<EndpointDataSource>();
        await context.Response.WriteAsJsonAsync(new
        {
            results = endpointDataSource
                .Endpoints
                .OfType<RouteEndpoint>()
                .Where(e => e.DisplayName?.StartsWith("gRPC") == true)
                .Select(e => new
                {
                    name = e.DisplayName,
                    pattern = e.RoutePattern.RawText,
                    order = e.Order
                })
                .ToList()
        });
    });

    endpoints.MapGet("/", async context =>
    {
        //await context.Response.WriteAsync("Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");
        await context.Response.WriteAsync("404 Page not found");
    });
});

app.Run();