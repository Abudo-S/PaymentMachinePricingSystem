using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.LoadBalancing;

namespace LibHelpers
{
    public class CustomLoadBalancerProxyProvider : IProxyConfigProvider
    {
        private static readonly NLog.Logger log = NLog.LogManager.GetCurrentClassLogger();

        private CustomMemoryConfig _config;

        public CustomLoadBalancerProxyProvider(string clusterId, List<string> nodesAddrs, string matchingPath = "{**catch-all}")
        {
            log.Info($"Initialized CustomLoadBalancerProxyProvider with clusterId: {clusterId}, matchingPath: {matchingPath}, nodes: {string.Join(", ", nodesAddrs)}");
            
            // Load a basic configuration
            // Should be based on your application needs.
            var routeConfig = new RouteConfig
            {
                RouteId = clusterId + "_route",
                ClusterId = clusterId,
                Match = new RouteMatch
                {
                    Path = matchingPath
                }
            };

            var routeConfigs = new[] { routeConfig };

            var destinationNodes = new Dictionary<string, DestinationConfig>();

            for (var i = 1; i <= nodesAddrs.Count; i++)
            {
                destinationNodes["destination" + i.ToString()] = new DestinationConfig {
                    Address = nodesAddrs[i - 1] 
                };
            }

            var clusterConfigs = new[]
            {
                new ClusterConfig
                {
                    ClusterId = clusterId,
                    LoadBalancingPolicy = LoadBalancingPolicies.RoundRobin,
                    Destinations = destinationNodes
                }
            };

            _config = new CustomMemoryConfig(routeConfigs, clusterConfigs);
        }

        public IProxyConfig GetConfig() => _config;

        /// <summary>
        /// Adjust proxy configuration Dynamically (ex. node joins or exists).
        /// </summary>
        public void Update(IReadOnlyList<RouteConfig> routes, IReadOnlyList<ClusterConfig> clusters)
        {
            var oldConfig = _config;
            _config = new CustomMemoryConfig(routes, clusters);
            oldConfig.SignalChange();
        }

        private class CustomMemoryConfig : IProxyConfig
        {
            private readonly CancellationTokenSource _cts = new CancellationTokenSource();

            public CustomMemoryConfig(IReadOnlyList<RouteConfig> routes, IReadOnlyList<ClusterConfig> clusters)
            {
                Routes = routes;
                Clusters = clusters;
                ChangeToken = new CancellationChangeToken(_cts.Token);
            }

            public IReadOnlyList<RouteConfig> Routes { get; }

            public IReadOnlyList<ClusterConfig> Clusters { get; }

            public IChangeToken ChangeToken { get; }

            internal void SignalChange()
            {
                _cts.Cancel();
            }
        }
    }
}
