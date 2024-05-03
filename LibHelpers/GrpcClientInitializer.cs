using Google.Api;
using Grpc.Net.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MicroservicesProtos;
using Grpc.Core;
using Grpc.Net.Client.Web;

namespace LibHelpers
{
    public class GrpcClientInitializer
    {
        private static readonly NLog.Logger log = NLog.LogManager.GetCurrentClassLogger();

        private Dictionary<string, ClientBase> nodesGrpcClients;

        #region Singleton
        private static readonly Lazy<GrpcClientInitializer> lazy =
            new Lazy<GrpcClientInitializer>(() => new GrpcClientInitializer());
        public static GrpcClientInitializer Instance { get { return lazy.Value; } }
        #endregion

        private GrpcClientInitializer() 
        {
            nodesGrpcClients = new();
        }

        public ClientBase BuildSingleGrpcClient<T>(string channelAddr, bool useHttp2 = false) where T : ClientBase
        {
            log.Info($"Building grpc client for channelAddr: {channelAddr}, useHttp2: {useHttp2}");

            HttpMessageHandler httpClientHandler = new HttpClientHandler();

            if (channelAddr.ToLower().Contains("loadbalancer"))
                httpClientHandler = new SubdirectoryHandler(new HttpClientHandler(), "/loadbalancer");

            GrpcWebHandler grpcWebhandler;

            if (!useHttp2)
            {
                grpcWebhandler = new GrpcWebHandler(GrpcWebMode.GrpcWebText, httpClientHandler);
                grpcWebhandler.HttpVersion = new Version(1, 1);
            }
            else
            {
                ////will ignore certificate validation errors, if you need that you may comment this section or make it configurable
                //httpClientHandler.ServerCertificateCustomValidationCallback = (message, cert, chain, sslPolicyErrors) =>
                //{
                //    return true;
                //};

                grpcWebhandler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, httpClientHandler);
                grpcWebhandler.HttpVersion = new Version(2, 0);
            }

            var httpClient = new HttpClient(grpcWebhandler);
            
            var protoChannel = GrpcChannel.ForAddress(channelAddr, new GrpcChannelOptions { HttpClient = httpClient });

            return (ClientBase)Activator.CreateInstance(typeof(T), protoChannel);
        }

        public void InitializeGrpcClients<T>(List<string> clusterNodesAddrs) where T : ClientBase
        {
            foreach (var nodeChannelAddr in clusterNodesAddrs)
            {
                try
                {
                    var grpcServiceClient = BuildSingleGrpcClient<T>(nodeChannelAddr);
                    nodesGrpcClients[nodeChannelAddr] = grpcServiceClient;
                }
                catch (Exception ex)
                {
                    log.Warn(ex, $"Can't create node client with: {nodeChannelAddr}");
                    nodesGrpcClients[nodeChannelAddr] = null;
                }
            }
        }

        /// <summary>
        /// if it notices an uninitialized node client, then it'll reinitialize it through InitializeGrpcClients()
        /// </summary>
        /// <param name="nodeAddr"></param>
        /// <returns></returns>
        public ClientBase GetNodeClient<T>(string nodeAddr) where T : ClientBase
        {
            ClientBase client = null;

            try
            {
                if (!nodesGrpcClients.ContainsKey(nodeAddr) || nodesGrpcClients[nodeAddr] == null)
                {
                    InitializeGrpcClients<T>(new List<string>() { nodeAddr});
                }

                client = nodesGrpcClients[nodeAddr];
            }
            catch (Exception ex)
            {
                log.Warn(ex, $"In GetNodeClient: {nodeAddr}");
            }

            if (client == null)
                throw new RpcException(Status.DefaultCancelled);

            return client;
        }

        /// <summary>
        /// if it notices an uninitialized node clients, then it'll reinitialize them through InitializeGrpcClients()
        /// </summary>
        /// <returns></returns>
        public List<ClientBase> GetAllNodesClients<T>() where T : ClientBase
        {
            List<ClientBase> clients = new();

            try
            {
                foreach (var clusterNodeAddr in nodesGrpcClients.Keys)
                {
                    var client = nodesGrpcClients[clusterNodeAddr];

                    if (client == null)
                    {
                        InitializeGrpcClients<T>(new List<string>() { clusterNodeAddr });
                    }

                    if (client != null)
                        clients.Add(client);
                }
            }
            catch (Exception ex)
            {
                log.Warn(ex, $"In GetAllNodesClients");
            }

            return clients;
        }
    }
}

public class SubdirectoryHandler : DelegatingHandler
{
    private readonly string _subdirectory;

    public SubdirectoryHandler(HttpMessageHandler innerHandler, string subdirectory)
        : base(innerHandler)
    {
        _subdirectory = subdirectory;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var old = request.RequestUri;

        var url = $"{old.Scheme}://{old.Host}:{old.Port}";
        url += $"{_subdirectory}{request.RequestUri.AbsolutePath}";
        request.RequestUri = new Uri(url, UriKind.Absolute);

        return base.SendAsync(request, cancellationToken);
    }
}
