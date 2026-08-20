using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Contract.Cli;

/// <summary>
/// Client for the Purr package registry API.
/// Provides search, download, and metadata operations.
/// </summary>
public class PurrClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    /// <summary>Production base URL.</summary>
    public const string ProductionUrl = "https://purr.finite.ovh/api/v1";

    /// <summary>Testing base URL.</summary>
    public const string TestingUrl = "http://testing.finite.ovh:8080/api/v1";

    public PurrClient(string? baseUrl = null)
    {
        _baseUrl = baseUrl ?? Environment.GetEnvironmentVariable("PURR_URL") ?? ProductionUrl;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    // --- Models ---

    public class PackageInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [JsonPropertyName("author")]
        public string? Author { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("tags")]
        public List<string>? Tags { get; set; }

        [JsonPropertyName("downloads")]
        public int Downloads { get; set; }

        [JsonPropertyName("gitUrl")]
        public string? GitUrl { get; set; }

        [JsonPropertyName("uploadedAt")]
        public string? UploadedAt { get; set; }

        [JsonPropertyName("updatedAt")]
        public string? UpdatedAt { get; set; }
    }

    public class PackageListResponse
    {
        [JsonPropertyName("package_count")]
        public int PackageCount { get; set; }

        [JsonPropertyName("packages")]
        public List<string> Packages { get; set; } = new();

        [JsonPropertyName("package_details")]
        public List<PackageInfo>? PackageDetails { get; set; }
    }

    public class StatsResponse
    {
        [JsonPropertyName("totalPackages")]
        public int TotalPackages { get; set; }

        [JsonPropertyName("totalDownloads")]
        public int TotalDownloads { get; set; }

        [JsonPropertyName("recentlyAdded")]
        public List<PackageInfo>? RecentlyAdded { get; set; }
    }

    // --- API Methods ---

    /// <summary>Search packages by query.</summary>
    public async Task<List<PackageInfo>> SearchAsync(string query, int limit = 20)
    {
        var url = $"{_baseUrl}/packages?search={Uri.EscapeDataString(query)}&details=true&pageSize={limit}";
        var resp = await _http.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PackageListResponse>(json, JsonOptions);
        return result?.PackageDetails ?? new();
    }

    /// <summary>Get package info by name (latest version).</summary>
    public async Task<PackageInfo?> GetPackageAsync(string name)
    {
        var url = $"{_baseUrl}/packages/{Uri.EscapeDataString(name)}";
        var resp = await _http.GetAsync(url);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<PackageInfo>(json, JsonOptions);
    }

    /// <summary>Get package info for a specific version.</summary>
    public async Task<PackageInfo?> GetPackageVersionAsync(string name, string version)
    {
        var url = $"{_baseUrl}/packages/{Uri.EscapeDataString(name)}/{Uri.EscapeDataString(version)}";
        var resp = await _http.GetAsync(url);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<PackageInfo>(json, JsonOptions);
    }

    /// <summary>Report a download (increments counter).</summary>
    public async Task ReportDownloadAsync(string name)
    {
        var url = $"{_baseUrl}/packages/{Uri.EscapeDataString(name)}/download";
        await _http.PostAsync(url, null);
    }

    /// <summary>Get repository statistics.</summary>
    public async Task<StatsResponse?> GetStatsAsync()
    {
        var url = $"{_baseUrl}/packages/statistics";
        var resp = await _http.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<StatsResponse>(json, JsonOptions);
    }

    /// <summary>Get popular tags.</summary>
    public async Task<List<string>> GetTagsAsync(int limit = 10)
    {
        var url = $"{_baseUrl}/packages/tags?limit={limit}";
        var resp = await _http.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? new();
    }

    /// <summary>Check if the registry is reachable.</summary>
    public async Task<bool> HealthCheckAsync()
    {
        try
        {
            var resp = await _http.GetAsync($"{_baseUrl}/health");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public void Dispose() => _http.Dispose();
}
