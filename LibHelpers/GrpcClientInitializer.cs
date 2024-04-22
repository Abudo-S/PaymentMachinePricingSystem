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

        private Dictionary<string, ClientBase> clusterGrpcClients;

        #region Singleton
        private static readonly Lazy<GrpcClientInitializer> lazy =
            new Lazy<GrpcClientInitializer>(() => new GrpcClientInitializer());
        public static GrpcClientInitializer Instance { get { return lazy.Value; } }
        #endregion

        private GrpcClientInitializer() 
        {
            clusterGrpcClients = new();
        }

        public ClientBase BuildSingleGrpcClient<T>(string channelAddr) where T : ClientBase
        {
            //prepare http client to ignore ssl certificate
            var httpClientHandler = new HttpClientHandler();


            //builder.Services.AddGrpcClient<DayRateServices.DayRateService>(o => { o.Address = new Uri("http://localhost:5039"); })
            //.ConfigureChannel(o =>
            //{
            //    o.HttpClient = new HttpClient(new GrpcWebHandler(GrpcWebMode.GrpcWebText, new
            //                  HttpClientHandler()));
            //});


            //will ignore certificate validation errors, if you need that you may comment this section or make it configurable
            //httpClientHandler.ServerCertificateCustomValidationCallback = (message, cert, chain, sslPolicyErrors) =>
            //{
            //    return true;
            //};

            var httpClient = new HttpClient(new GrpcWebHandler(GrpcWebMode.GrpcWeb, httpClientHandler));

            var protoChannel = GrpcChannel.ForAddress(channelAddr, new GrpcChannelOptions { HttpClient = httpClient });

            return (ClientBase)Activator.CreateInstance(typeof(T), protoChannel);
        }

        public void InitializeClusterGrpcClients<T>(List<string> clusterNodesAddrs) where T : ClientBase
        {
            foreach (var nodeChannelAddr in clusterNodesAddrs)
            {
                try
                {
                    var grpcServiceClient = BuildSingleGrpcClient<T>(nodeChannelAddr);
                    clusterGrpcClients[nodeChannelAddr] = grpcServiceClient;
                }
                catch (Exception ex) 
                {
                    log.Warn(ex, $"Can't create node client with: {nodeChannelAddr}");
                    clusterGrpcClients[nodeChannelAddr] = null;
                }
            }
        }

        /// <summary>
        /// if it notices an uninitialized node client, then it'll reinitialize it through InitializeClusterGrpcClients()
        /// </summary>
        /// <param name="clusterNodesAddr"></param>
        /// <returns></returns>
        public ClientBase GetClusterNodeClient<T>(string clusterNodesAddr) where T : ClientBase
        {
            ClientBase client = null;

            try
            {
                if (clusterGrpcClients[clusterNodesAddr] == null)
                {
                    InitializeClusterGrpcClients<T>(new List<string>() { clusterNodesAddr});
                }

                client = clusterGrpcClients[clusterNodesAddr];
            }
            catch (Exception ex)
            {
                log.Warn(ex, $"In GetClusterNodeClient: {clusterNodesAddr}");
            }

            return client;
        }

        /// <summary>
        /// if it notices an uninitialized node clients, then it'll reinitialize them through InitializeClusterGrpcClients()
        /// </summary>
        /// <param name="clusterNodesAddr"></param>
        /// <returns></returns>
        public List<ClientBase> GetAllClusterNodesClients<T>(string clusterNodesAddr) where T : ClientBase
        {
            List<ClientBase> clients = new();

            try
            {
                foreach (var clusterNodeAddr in clusterNodesAddr)
                {
                    var client = clusterGrpcClients[clusterNodesAddr];

                    if (client == null)
                    {
                        InitializeClusterGrpcClients<T>(new List<string>() { clusterNodesAddr });
                    }

                    if (client != null)
                        clients.Add(client);
                }
            }
            catch (Exception ex)
            {
                log.Warn(ex, $"In GetClusterNodeClient: {clusterNodesAddr}");
            }

            return clients;
        }
    }
}
