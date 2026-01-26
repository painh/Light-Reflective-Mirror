using Grapevine;
using LightReflectiveMirror.Compression;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace LightReflectiveMirror.Endpoints
{
    [Serializable]
    struct RelayStats
    {
        public int ConnectedClients;
        public int RoomCount;
        public int PublicRoomCount;
        public TimeSpan Uptime;
        public string BuildTime;
    }

    [RestResource]
    public class Endpoint
    {
        private static string _cachedServerList = "[]";
        private static string _cachedCompressedServerList;
        public static DateTime lastPing = DateTime.Now;

        // 허용된 IP 목록 (환경변수 ALLOWED_IPS로 설정, 쉼표로 구분)
        private static HashSet<string> _allowedIPs = null;

        private static List<Room> _rooms { get => Program.instance.GetRooms().Where(x => x.isPublic).ToList(); }

        private static HashSet<string> GetAllowedIPs()
        {
            if (_allowedIPs == null)
            {
                var allowedIPsEnv = Environment.GetEnvironmentVariable("ALLOWED_IPS");
                if (!string.IsNullOrEmpty(allowedIPsEnv))
                {
                    _allowedIPs = allowedIPsEnv
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(ip => ip.Trim())
                        .ToHashSet();
                }
                else
                {
                    _allowedIPs = new HashSet<string>(); // 빈 목록 = 모두 허용
                }
            }
            return _allowedIPs;
        }

        private static bool IsIPAllowed(IHttpContext context)
        {
            var allowedIPs = GetAllowedIPs();
            if (allowedIPs.Count == 0) return true; // 설정 안되면 모두 허용

            var remoteIP = context.Request.RemoteEndPoint?.Address?.ToString();
            if (string.IsNullOrEmpty(remoteIP)) return false;

            // IPv6 localhost 처리
            if (remoteIP == "::1") remoteIP = "127.0.0.1";

            // 127.0.0.1은 항상 허용
            if (remoteIP == "127.0.0.1") return true;

            return allowedIPs.Contains(remoteIP) || allowedIPs.Contains("*");
        }

        private RelayStats _stats { get => new()
        {
            ConnectedClients = Program.instance.GetConnections(),
            RoomCount = Program.instance.GetRooms().Count,
            PublicRoomCount = Program.instance.GetPublicRoomCount(),
            Uptime = Program.instance.GetUptime(),
            BuildTime = Program.GitCommitTime
        }; }

        public static void RoomsModified()
        {
            _cachedServerList = JsonConvert.SerializeObject(_rooms, Formatting.Indented);
            _cachedCompressedServerList = _cachedServerList.Compress();

            if (Program.conf.UseLoadBalancer)
                Program.instance.UpdateLoadBalancerServers();
        }

        [RestRoute("Get", "/api/stats")]
        public async Task Stats(IHttpContext context)
        {
            lastPing = DateTime.Now;
            string json = JsonConvert.SerializeObject(_stats, Formatting.Indented);
            await context.Response.SendResponseAsync(json);
        }

        [RestRoute("Get", "/api/servers")]
        public async Task ServerList(IHttpContext context)
        {
            if (Program.conf.EndpointServerList)
            {
                await context.Response.SendResponseAsync(_cachedServerList);
            }
            else
                await context.Response.SendResponseAsync(HttpStatusCode.Forbidden);
        }

        [RestRoute("Get", "/api/compressed/servers")]
        public async Task ServerListCompressed(IHttpContext context)
        {
            context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
            context.Response.Headers.Add("Access-Control-Allow-Methods", "POST, GET, OPTIONS");

            if (Program.conf.EndpointServerList)
            {
                await context.Response.SendResponseAsync(_cachedCompressedServerList);
            }
            else
                await context.Response.SendResponseAsync(HttpStatusCode.Forbidden);
        }

        [RestRoute("Options", "/api/compressed/servers")]
        public async Task ServerListCompressedOptions(IHttpContext context)
        {
            var originHeaders = context.Request.Headers["Access-Control-Request-Headers"];

            context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
            context.Response.Headers.Add("Access-Control-Allow-Methods", "POST, GET, OPTIONS");
            context.Response.Headers.Add("Access-Control-Allow-Headers", originHeaders);

            await context.Response.SendResponseAsync(HttpStatusCode.Ok);
        }

        [RestRoute("Get", "/api/health")]
        public async Task Health(IHttpContext context)
        {
            await context.Response.SendResponseAsync("{\"status\":\"ok\"}");
        }

        [RestRoute("Get", "/api/logs")]
        public async Task Logs(IHttpContext context)
        {
            if (!IsIPAllowed(context))
            {
                await context.Response.SendResponseAsync(HttpStatusCode.Forbidden);
                return;
            }

            context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
            context.Response.Headers.Add("Access-Control-Allow-Methods", "GET, OPTIONS");

            int count = 100;
            long? beforeId = null;

            string countStr = context.Request.QueryString["count"];
            if (!string.IsNullOrEmpty(countStr) && int.TryParse(countStr, out int parsedCount))
            {
                count = Math.Min(parsedCount, 500); // 최대 500개로 제한
            }

            string beforeStr = context.Request.QueryString["before"];
            if (!string.IsNullOrEmpty(beforeStr) && long.TryParse(beforeStr, out long parsedBefore))
            {
                beforeId = parsedBefore;
            }

            var logs = LogStore.Instance.GetLogs(count, beforeId);
            var response = new
            {
                logs = logs,
                totalCount = LogStore.Instance.Count,
                hasMore = logs.Count > 0 && logs.Count == count
            };

            string json = JsonConvert.SerializeObject(response, Formatting.Indented);
            await context.Response.SendResponseAsync(json);
        }

        [RestRoute("Options", "/api/logs")]
        public async Task LogsOptions(IHttpContext context)
        {
            var originHeaders = context.Request.Headers["Access-Control-Request-Headers"];

            context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
            context.Response.Headers.Add("Access-Control-Allow-Methods", "GET, OPTIONS");
            context.Response.Headers.Add("Access-Control-Allow-Headers", originHeaders);

            await context.Response.SendResponseAsync(HttpStatusCode.Ok);
        }

        [RestRoute("Get", "/")]
        public async Task Dashboard(IHttpContext context)
        {
            if (!IsIPAllowed(context))
            {
                await context.Response.SendResponseAsync(HttpStatusCode.Forbidden);
                return;
            }

            try
            {
                // Try to load from embedded resource or file
                string html = LoadDashboardHtml();
                context.Response.ContentType = Grapevine.ContentType.Html;
                await context.Response.SendResponseAsync(html);
            }
            catch (Exception e)
            {
                await context.Response.SendResponseAsync($"Dashboard error: {e.Message}");
            }
        }

        private static string _cachedDashboardHtml = null;

        private string LoadDashboardHtml()
        {
            if (_cachedDashboardHtml != null)
                return _cachedDashboardHtml;

            // Try to load from file first (for development)
            var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "web", "index.html");
            if (File.Exists(filePath))
            {
                _cachedDashboardHtml = File.ReadAllText(filePath);
                return _cachedDashboardHtml;
            }

            // Fallback: simple dashboard
            _cachedDashboardHtml = @"<!DOCTYPE html>
<html>
<head><title>LRM Relay Server</title></head>
<body style='font-family:sans-serif;background:#1a1a2e;color:#eee;padding:40px;'>
<h1>LRM Relay Server</h1>
<p>Dashboard HTML not found. Check /web/index.html</p>
<p><a href='/api/stats' style='color:#4ade80;'>View Stats API</a></p>
<p><a href='/api/servers' style='color:#4ade80;'>View Servers API</a></p>
</body>
</html>";
            return _cachedDashboardHtml;
        }
    }

    public class EndpointServer
    {
        public bool Start(ushort port = 8080)
        {
            try
            {
                var config = new ConfigurationBuilder()
                .SetBasePath(System.IO.Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .Build();

                var server = new RestServerBuilder(new ServiceCollection(), config, 
                (services) =>
                {
                    services.AddLogging(configure => configure.AddConsole());
                    services.Configure<LoggerFilterOptions>(options => options.MinLevel = Microsoft.Extensions.Logging.LogLevel.None);
                }, (server) =>
                {
                    server.Prefixes.Add($"http://*:{port}/");
                }).Build();

                server.Router.Options.SendExceptionMessages = false;
                server.Start();

                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return false;
            }

        }
    }
}

