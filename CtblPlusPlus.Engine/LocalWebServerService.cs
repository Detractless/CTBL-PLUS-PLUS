using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Linq;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using CtblPlusPlus.Core.Interfaces.Data;

namespace CtblPlusPlus.Engine
{
    public class LocalWebServerService : BackgroundService
    {
        private readonly ILogger<LocalWebServerService> _logger;
        private readonly HttpListener _listener;
        private readonly string _webRoot;
        private readonly ISettingsRepository _settingsRepository;
        private readonly IQueueRepository _queueRepository;

        public LocalWebServerService(
            ILogger<LocalWebServerService> logger, 
            ISettingsRepository settingsRepository, 
            IQueueRepository queueRepository)
        {
            _logger = logger;
            _settingsRepository = settingsRepository;
            _queueRepository = queueRepository;
            _listener = new HttpListener();
            _listener.Prefixes.Add("http://localhost:58123/");
            _listener.Prefixes.Add("http://127.0.0.1:58123/");
            
            // Prioritize the local dev directory if it exists, otherwise use the installation directory
            string devPath = @"C:\Users\Calibro1\Downloads\CTBL ++ Version 0.1.1.4-W\CTBL ++ Version 0.1.0.4\Unobfuscated_Backup\web";
            if (Directory.Exists(devPath))
            {
                _webRoot = devPath;
                _logger.LogInformation($"LocalWebServerService: Using DEV web root: {_webRoot}");
            }
            else
            {
                _webRoot = @"C:\Program Files\Cold Turkey\web";
                _logger.LogInformation($"LocalWebServerService: Using PROD web root: {_webRoot}");
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _listener.Start();
                _logger.LogInformation("LocalWebServerService started on http://localhost:58123/ and 127.0.0.1");

                while (!stoppingToken.IsCancellationRequested)
                {
                    var context = await _listener.GetContextAsync();
                    _ = ProcessRequestAsync(context);
                }
            }
            catch (Exception ex) when (ex is HttpListenerException || ex is OperationCanceledException)
            {
                // Normal shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in LocalWebServerService");
            }
            finally
            {
                _listener.Close();
            }
        }

        private async Task ProcessRequestAsync(HttpListenerContext context)
        {
            try
            {
                var request = context.Request;
                var response = context.Response;

                // Add CORS headers to allow file:/// to fetch from localhost
                response.Headers.Add("Access-Control-Allow-Origin", "*");
                response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

                if (request.HttpMethod == "OPTIONS")
                {
                    response.StatusCode = (int)HttpStatusCode.OK;
                    response.Close();
                    return;
                }

                string relativePath = request.Url.LocalPath.TrimStart('/');

                if (relativePath.StartsWith("api/"))
                {
                    await ProcessApiRequestAsync(context, relativePath);
                    return;
                }

                if (string.IsNullOrEmpty(relativePath))
                {
                    relativePath = "index.html";
                }

                // Prevent directory traversal
                relativePath = relativePath.Replace("..", "").Replace("/", "\\");

                string filePath = Path.Combine(_webRoot, relativePath);

                if (File.Exists(filePath))
                {
                    byte[] fileBytes = await File.ReadAllBytesAsync(filePath);
                    
                    if (filePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                        response.ContentType = "text/html; charset=utf-8";
                    else if (filePath.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
                        response.ContentType = "application/javascript; charset=utf-8";
                    else if (filePath.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
                        response.ContentType = "text/css; charset=utf-8";

                    response.StatusCode = (int)HttpStatusCode.OK;
                    response.ContentLength64 = fileBytes.Length;
                    await response.OutputStream.WriteAsync(fileBytes, 0, fileBytes.Length);
                }
                else
                {
                    response.StatusCode = (int)HttpStatusCode.NotFound;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing request");
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            }
            finally
            {
                try { context.Response.Close(); } catch { }
            }
        }

        private async Task ProcessApiRequestAsync(HttpListenerContext context, string relativePath)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                string callback = request.QueryString["callback"];
                bool isJsonp = !string.IsNullOrEmpty(callback);
                string jsonResponse = "";

                if ((request.HttpMethod == "GET" || isJsonp) && relativePath == "api/blocks/queued-delay")
                {
                    _logger.LogInformation("API Hit: GET /api/blocks/queued-delay");
                    var pendingRequests = _queueRepository.GetPendingRequests();
                    var activeDelays = pendingRequests
                        .Where(r => r.TargetUrl == "CTBL_QUEUED_DELAY")
                        .Select(r => new { r.BlockName, r.UnlockAt, r.RequestedAt })
                        .ToList();
                    jsonResponse = JsonSerializer.Serialize(activeDelays);
                }
                else if ((request.HttpMethod == "POST" || (request.HttpMethod == "GET" && isJsonp)) && relativePath == "api/settings/global-delay")
                {
                    _logger.LogInformation("API Hit: POST/GET /api/settings/global-delay");
                    
                    if (isJsonp)
                    {
                        string delay = request.QueryString["delay"];
                        _logger.LogInformation($"Received payload delay: {delay}");
                        
                        if (!string.IsNullOrEmpty(delay) && int.TryParse(delay, out int delayMinutes))
                        {
                            string hours = (delayMinutes / 60.0).ToString(System.Globalization.CultureInfo.InvariantCulture);
                            _settingsRepository.SetSetting("GlobalDelayHours", hours);
                            _logger.LogInformation($"Set GlobalDelayHours to {hours}");
                        }
                    }
                    else
                    {
                        using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                        {
                            string body = await reader.ReadToEndAsync();
                            _logger.LogInformation($"Received payload: {body}");
                            // Non-JSONP support could be parsed from the JSON body here if needed.
                        }
                    }

                    jsonResponse = "{\"status\": \"success\"}";
                }
                else if ((request.HttpMethod == "GET" || isJsonp) && relativePath == "api/settings/queued-delay-blocks")
                {
                    _logger.LogInformation("API Hit: GET /api/settings/queued-delay-blocks");
                    string blocksJson = _settingsRepository.GetSetting("QueuedDelayBlocks", "[]");
                    jsonResponse = blocksJson;
                }
                else if ((request.HttpMethod == "POST" || (request.HttpMethod == "GET" && isJsonp)) && relativePath == "api/settings/toggle-queued-delay")
                {
                    _logger.LogInformation("API Hit: POST/GET /api/settings/toggle-queued-delay");
                    if (isJsonp)
                    {
                        string blockName = request.QueryString["block"];
                        string enabled = request.QueryString["enabled"];
                        _logger.LogInformation($"Received payload block: {blockName}, enabled: {enabled}");
                        
                        if (!string.IsNullOrEmpty(blockName) && !string.IsNullOrEmpty(enabled))
                        {
                            blockName = System.Net.WebUtility.UrlDecode(blockName);
                            string currentJson = _settingsRepository.GetSetting("QueuedDelayBlocks", "[]");
                            var list = JsonSerializer.Deserialize<System.Collections.Generic.List<string>>(currentJson) ?? new System.Collections.Generic.List<string>();
                            
                            if (enabled.ToLower() == "true" && !list.Contains(blockName))
                            {
                                list.Add(blockName);
                            }
                            else if (enabled.ToLower() == "false")
                            {
                                list.Remove(blockName);
                            }
                            
                            _settingsRepository.SetSetting("QueuedDelayBlocks", JsonSerializer.Serialize(list));
                        }
                    }
                    jsonResponse = "{\"status\": \"success\"}";
                }
                else if ((request.HttpMethod == "POST" || (request.HttpMethod == "GET" && isJsonp)) && relativePath == "api/blocks/enqueue-queued-delay")
                {
                    _logger.LogInformation("API Hit: POST/GET /api/blocks/enqueue-queued-delay");
                    if (isJsonp)
                    {
                        string blockName = request.QueryString["block"];
                        _logger.LogInformation($"Received payload enqueue block: {blockName}");
                        
                        if (!string.IsNullOrEmpty(blockName))
                        {
                            blockName = System.Net.WebUtility.UrlDecode(blockName);
                            string delayStr = _settingsRepository.GetSetting("GlobalDelayHours", "24");
                            double.TryParse(delayStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double delayHours);
                            if (delayHours <= 0) delayHours = 24;
                            
                            _queueRepository.AddRequest(new CtblPlusPlus.Models.DelayRequest
                            {
                                Id = Guid.NewGuid().ToString("N"),
                                BlockName = blockName,
                                TargetUrl = "CTBL_QUEUED_DELAY",
                                RequestedAt = DateTime.UtcNow,
                                UnlockAt = DateTime.UtcNow.AddHours(delayHours),
                                Status = "Pending"
                            });
                        }
                    }
                    jsonResponse = "{\"status\": \"success\"}";
                }
                else
                {
                    _logger.LogWarning($"API Hit: Endpoint not found: {request.HttpMethod} {relativePath}");
                    response.StatusCode = (int)HttpStatusCode.NotFound;
                    return;
                }

                if (isJsonp)
                {
                    jsonResponse = $"{callback}({jsonResponse});";
                    response.ContentType = "application/javascript";
                }
                else
                {
                    response.ContentType = "application/json";
                }

                byte[] buffer = System.Text.Encoding.UTF8.GetBytes(jsonResponse);
                response.StatusCode = (int)HttpStatusCode.OK;
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing API request: {relativePath}");
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
            }
            finally
            {
                try { context.Response.Close(); } catch { }
            }
        }
    }
}
