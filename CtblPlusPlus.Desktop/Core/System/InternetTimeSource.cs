using System;
using System.Net.Http;
using CtblPlusPlus.Core.Interfaces.System;

namespace CtblPlusPlus.Core.AppSystem;

public class InternetTimeSource : ITimeSource
{
    private static readonly string[] Sources = new[] 
    { 
        "https://www.google.com", 
        "https://www.microsoft.com", 
        "https://www.apple.com",
        "https://www.facebook.com",
        "https://www.nist.gov",
        "https://www.ibm.com",
        "https://www.yahoo.com",
        "https://www.cloudflare.com",
        "https://store.steampowered.com"
    };
    
    private int _currentIndex = 0;
    private readonly HttpClient _client;

    public InternetTimeSource()
    {
        var handler = new HttpClientHandler
        {
            SslProtocols = global::System.Security.Authentication.SslProtocols.Tls12
        };
        _client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("CtblPlusPlus/1.0");
    }

    public DateTime? GetUtcTime(out string sourceName)
    {
        sourceName = "None";
        
        // Try up to 3 sources in a row if they fail
        for (int i = 0; i < 3; i++)
        {
            string url = Sources[_currentIndex];
            _currentIndex = (_currentIndex + 1) % Sources.Length;
            
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Head, url);
                using var response = _client.Send(request);
                if (response.Headers.Date.HasValue)
                {
                    sourceName = url;
                    return response.Headers.Date.Value.UtcDateTime;
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to reach {url}: {ex.Message}");
            }
        }

        return null;
    }

    private void Log(string message)
    {
        try
        {
            string logPath = global::System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CtblPlusPlus", "process_log.txt");
            global::System.IO.File.AppendAllText(logPath, $"{DateTime.Now:O}: [InternetTimeSource] {message}{Environment.NewLine}");
        }
        catch { }
    }
}


