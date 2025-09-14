using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;

namespace YoutubeDownloader.Core.Utils;

public static class Http
{
    private static HttpClient? _customClient;
    private static readonly object _lock = new object();

    // Predefined user agents
    public static class UserAgents
    {
        public static string Default =>
            $"YoutubeDownloader/{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}";
        public static string Chrome =>
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
        public static string Firefox =>
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/121.0";
        public static string ChromeMobile =>
            "Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36";
        public static string Safari =>
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_0) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Safari/605.1.15";
        public static string Edge =>
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 Edg/120.0.0.0";
    }

    private static HttpClient CreateDefaultClient()
    {
        return new HttpClient
        {
            DefaultRequestHeaders =
            {
                // Use the original default user agent
                UserAgent =
                {
                    new ProductInfoHeaderValue(
                        "YoutubeDownloader",
                        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
                    ),
                },
            },
        };
    }

    private static HttpClient CreateClientWithUserAgent(string userAgent)
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent", userAgent);
        return client;
    }

    public static HttpClient Client
    {
        get
        {
            lock (_lock)
            {
                return _customClient ?? CreateDefaultClient();
            }
        }
    }

    /// <summary>
    /// Set a custom user agent for all HTTP requests
    /// </summary>
    /// <param name="userAgent">The user agent string to use, or null to reset to default</param>
    public static void SetUserAgent(string? userAgent)
    {
        lock (_lock)
        {
            _customClient?.Dispose();
            _customClient = userAgent != null ? CreateClientWithUserAgent(userAgent) : null;
        }
    }

    /// <summary>
    /// Create a new HttpClient with a specific user agent (doesn't affect the global client)
    /// </summary>
    public static HttpClient CreateClient(string? userAgent = null)
    {
        return userAgent != null ? CreateClientWithUserAgent(userAgent) : CreateDefaultClient();
    }
}
