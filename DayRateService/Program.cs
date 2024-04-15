using Google.Api;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using DayRateServices = DayRateService.Services;

//IConfiguration configuration = new ConfigurationBuilder()
//        .SetBasePath(Directory.GetCurrentDirectory())
//        .AddJsonFile("appSettings.json", optional: false, reloadOnChange: false)
//        .Build();

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddGrpc();//.AddServiceOptions(options => options.);
builder.Services.AddGrpc().AddJsonTranscoding();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("DayRateLoadBalancer"));

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

//builder.WebHost.UseKestrel(option =>
//{
//    option.ListenAnyIP(80, config =>
//    {
//        config.Protocols = HttpProtocols.Http1AndHttp2;
//        //config.UseHttps();
//    });
//});

//Redis
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
    options.InstanceName = "DayRateService_Redis";
});
builder.Services.AddScoped<DayRateServices.DayRateService>();
//builder.Services.AddSingleton<DayRateServices.DayRateService>();

builder.Services.Configure<KestrelServerOptions>(options => {
    options.ConfigureHttpsDefaults(options =>
        options.ClientCertificateMode = ClientCertificateMode.RequireCertificate);
});


var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseRouting();

//app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

// must be added after UseRouting and before UseEndpoints 
//we will set default to true, to enable defualt GRPC-WEB interface on each GRPC.addService
app.UseGrpcWeb(new GrpcWebOptions() { DefaultEnabled = true });

app.UseCors();
app.UseEndpoints(endpoints =>
{
    endpoints.MapGrpcService<DayRateServices.DayRateService>().EnableGrpcWeb().RequireCors("AllowAll");

    endpoints.MapReverseProxy();

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
