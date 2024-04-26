using AutoMapper;
using LibDTO.Config;
using LibHelpers;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Caching.Distributed;
using RequestHandlerMiddleware.Services;


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddGrpc();//.AddServiceOptions(options => options.);
builder.Services.AddGrpc().AddJsonTranscoding();

//builder.Services.AddHealthChecks();

builder.Services.AddCors(o => o.AddPolicy("AllowAll", builder =>
{
    builder.AllowAnyOrigin()
           .AllowAnyMethod()
           .AllowAnyHeader()
           .WithExposedHeaders("Grpc-Status", "Grpc-Message", "Grpc-Encoding", "Grpc-Accept-Encoding");
}));

//Response time optimization
builder.Services.AddResponseCompression(opts =>
{
    opts.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
        new[] { "application/octet-stream" });
});

//if enabled, we can't overide ports through docker's container-command
builder.WebHost.UseKestrel(option =>
{
    option.ListenAnyIP(81, config =>
    {
        config.Protocols = HttpProtocols.Http1AndHttp2;
    });
    //if enabled YARP's HTTP request verion should be "2.0" + TLS certificate should be configured
    //option.ListenAnyIP(81, config =>
    //{
    //    config.Protocols = HttpProtocols.Http1AndHttp2;
    //    config.UseHttps();
    //});
});

//Dependency injection
builder.Services.AddScoped<RequestHandlerMiddlewareService>();

//builder.Services.Configure<KestrelServerOptions>(options => {
//    options.ConfigureHttpsDefaults(options =>
//        options.ClientCertificateMode = ClientCertificateMode.NoCertificate);
//    //options.ConfigureEndpointDefaults(lo => lo.Protocols = HttpProtocols.Http1AndHttp2);
//});


var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseRouting();

app.UseStaticFiles();

app.UseRouting();

// must be added after UseRouting and before UseEndpoints 
//new GrpcWebOptions() { DefaultEnabled = true } -> applies default options that changes headers -cause compression error to YARP
//app.UseGrpcWeb(new GrpcWebOptions() { DefaultEnabled = true });
app.UseGrpcWeb();

app.UseCors();

app.MapGrpcService<RequestHandlerMiddlewareService>().EnableGrpcWeb().RequireCors("AllowAll");

app.UseEndpoints(endpoints =>
{
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