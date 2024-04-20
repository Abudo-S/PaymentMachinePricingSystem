using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Health;
using Yarp.ReverseProxy.LoadBalancing;

namespace LibHelpers
{
    public class CustomLoadBalancerProxyProvider : IProxyConfigProvider
    {
        private static readonly NLog.Logger log = NLog.LogManager.GetCurrentClassLogger();
        private static IConfiguration configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .Build();

        private string clusterId;
        private string matchingPath;

        private CustomMemoryConfig _config;

        public CustomLoadBalancerProxyProvider(List<string> nodesAddrs, bool useHTTP2 = false)
        {
            log.Info("Invoked CustomLoadBalancerProxyProvider()");

            //get config params
            clusterId = (string)(configuration.GetValue(typeof(string), "ClusterId") ?? "DayRateCluster");
            matchingPath = (string)(configuration.GetValue(typeof(string), "MatchingPath") ?? "{**catch-all}");
            
            log.Debug($"Initialized CustomLoadBalancerProxyProvider with clusterId: {clusterId}, matchingPath: {matchingPath}, nodes: {string.Join(", ", nodesAddrs)}");

            var kvpClusterConfig = PrepareClusterConfigRoutes(nodesAddrs, useHTTP2);

            _config = new CustomMemoryConfig(kvpClusterConfig.Key, kvpClusterConfig.Value);
        }

        public KeyValuePair<IReadOnlyList<RouteConfig>, IReadOnlyList<ClusterConfig>> PrepareClusterConfigRoutes(List<string> nodesAddrs, bool useHTTP2 = false)
        {
            // Load a basic configuration
            // Should be based on your application needs.
            var routeConfig = new RouteConfig
            {
                RouteId = clusterId + "_route",
                ClusterId = clusterId,
                Match = new RouteMatch
                {
                    Path = matchingPath
                },
                Transforms = new List<Dictionary<string, string>>()
                {
                    new Dictionary<string, string>()
                    {
                        { "PathPattern", matchingPath.Replace("LoadBalancer/", "")}
                    },
                    new Dictionary<string, string>()
                    {
                        { "RequestHeadersCopy", true.ToString()}
                    },
                    new Dictionary<string, string>()
                    {
                        { "RequestHeaderOriginalHost", true.ToString()}
                    }
                }
            };

            var routeConfigs = new[] { routeConfig };

            var destinationNodes = new Dictionary<string, DestinationConfig>();

            for (var i = 1; i <= nodesAddrs.Count; i++)
            {
                destinationNodes["destination" + i.ToString()] = new DestinationConfig
                {
                    Address = nodesAddrs[i - 1]
                };
            }

            //passive circuit breaker
            //blocks temporaly the traffic for a cluster's node after a certain failure-policy rate
            var healthCheck = new HealthCheckConfig
            {
                Passive = new PassiveHealthCheckConfig
                {
                    Enabled = true,
                    Policy = HealthCheckConstants.PassivePolicy.TransportFailureRate,
                    ReactivationPeriod = TimeSpan.FromMinutes(2)
                }
            };

            var metadata = new Dictionary<string, string> {
                { TransportFailureRateHealthPolicyOptions.FailureRateLimitMetadataName, "0.5" }
            };

            //HTTP/2 for grpc
            var httpRequestConfigs = new ForwarderRequestConfig()
            {
                Version = new Version(useHTTP2 ? "2.0" : "1.1"), //should be 2.0 in case of https listening
                VersionPolicy = HttpVersionPolicy.RequestVersionExact
            };

            var clusterConfigs = new[]
            {
                new ClusterConfig
                {
                    ClusterId = clusterId,
                    LoadBalancingPolicy = LoadBalancingPolicies.RoundRobin,
                    Destinations = destinationNodes,
                    HealthCheck = healthCheck,
                    Metadata = metadata,
                    HttpRequest = httpRequestConfigs
                }
            };

            return KeyValuePair.Create((IReadOnlyList<RouteConfig>)routeConfigs, (IReadOnlyList<ClusterConfig>)clusterConfigs);
        }

        public IProxyConfig GetConfig() => _config;

        /// <summary>
        /// Adjust proxy configuration Dynamically (ex. node joins or exists).
        /// </summary>
        public void Update(List<string> nodesAddrs)
        {
            var oldConfig = _config;

            var kvpClusterConfig = PrepareClusterConfigRoutes(nodesAddrs);

            _config = new CustomMemoryConfig(kvpClusterConfig.Key, kvpClusterConfig.Value);
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
