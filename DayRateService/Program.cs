using Google.Api;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using DayRateServices = DayRateService.Services;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Configuration;
using Microsoft.AspNetCore.Builder;
using LibHelpers;
using System.Collections;
using Grpc.Net.Client.Web;
using DayRateService;
using Microsoft.Extensions.Configuration;
using DayRateService.DbServices;
using AutoMapper;
using LibDTO.Generic;
using Microsoft.Extensions.Caching.Distributed;
using System.Net.Sockets;
using System.Net;
using LibDTO.Config;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddGrpc();//.AddServiceOptions(options => options.);
builder.Services.AddGrpc().AddJsonTranscoding();

//builder.Services.AddReverseProxy()
//    .LoadFromConfig(builder.Configuration.GetSection("DayRateLoadBalancer"));

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
//builder.WebHost.UseKestrel(option =>
//{
//    option.ListenAnyIP(8080, config =>
//    {
//        config.Protocols = HttpProtocols.Http1AndHttp2;
//    });
//    //if enabled YARP's HTTP request verion should be "2.0" + TLS certificate should be configured
//    //option.ListenAnyIP(81, config =>
//    //{
//    //    config.Protocols = HttpProtocols.Http1AndHttp2;
//    //    config.UseHttps();
//    //});
//});

//Redis
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
    options.InstanceName = "DayRateService_Redis";
});

//MongoDB
builder.Services.Configure<PricingSystemDataBaseConfig>(
    builder.Configuration.GetSection("PricingSystemDataBase"));
builder.Services.AddSingleton<DayRateDbService>();

//AutoMapper proto<->DTO
var mapperConfig = new MapperConfiguration(mc =>
{
    mc.AddProfile(new GenericMappingProfile());
});
IMapper mapper = mapperConfig.CreateMapper();
builder.Services.AddSingleton(mapper);

//Dependency injection
builder.Services.AddScoped<DayRateServices.DayRateService>();
var machineIP = Dns.GetHostByName(Dns.GetHostName()).AddressList.First(address => address.AddressFamily == AddressFamily.InterNetwork).ToString();
List<string> clusterNodes = new List<string>();
builder.Configuration.GetSection("ClusterNodes").Bind(clusterNodes);
//exclude current node
clusterNodes = clusterNodes.Where(clusterNode => !clusterNode.Contains(machineIP)).ToList();
var customLBPP = new CustomLoadBalancerProxyProvider(clusterNodes);
builder.Services.AddSingleton<IProxyConfigProvider>(customLBPP).AddReverseProxy();


DayRateManager.Instance.Init<MicroservicesProtos.DayRate.DayRateClient>(customLBPP, 
    (DayRateDbService)builder.Services.BuildServiceProvider().GetRequiredService(typeof(DayRateDbService)),
    machineIP,
    (IDistributedCache)builder.Services.BuildServiceProvider().GetRequiredService(typeof(IDistributedCache)),
    clusterNodes,
    mapper,
    (string)(builder.Configuration.GetValue(typeof(string), "MiddlewareEndpoint") ?? 15000),
    (int)(builder.Configuration.GetValue(typeof(int), "MaxThreads") ?? 3),
    (int)(builder.Configuration.GetValue(typeof(int), "RequestExpiryInMilliseconds") ?? 15000));

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
app.MapReverseProxy();

// must be added after UseRouting and before UseEndpoints 
//new GrpcWebOptions() { DefaultEnabled = true } -> applies default options that changes headers -cause compression error to YARP
//app.UseGrpcWeb(new GrpcWebOptions() { DefaultEnabled = true });
app.UseGrpcWeb();

app.UseCors();

//NOTE: native grpc service uri are reserved through MapGrpcService(), so YARP can't intercept them
app.MapGrpcService<DayRateServices.DayRateService>().EnableGrpcWeb().RequireCors("AllowAll");

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

