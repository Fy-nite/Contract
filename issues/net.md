# New Module: Net - Networking

## [New Module] `Net` - HTTP client and networking

HTTP client and basic networking primitives.

API calls, web scraping, downloading files. Currently no networking support.

### Proposed Signatures

```
Net.HttpGet(string url) -> string
Net.HttpGetBytes(string url) -> int[]
Net.HttpPost(string url, string body) -> string
Net.HttpPostJson(string url, string json) -> string
Net.HttpPut(string url, string body) -> string
Net.HttpDelete(string url) -> string
Net.DownloadFile(string url, string path) -> void
Net.UploadFile(string url, string path, string fieldName) -> string
Net.SetHeader(string name, string value) -> void
Net.ClearHeaders() -> void
Net.SetTimeout(int ms) -> void
Net.SetBaseUrl(string url) -> void
Net.GetResponseCode() -> int
Net.GetResponseHeaders() -> Dict
```

### Examples

```
var data = Net.HttpGet("https://api.example.com/users");
Net.HttpPostJson("https://api.example.com/users", "{ \"name\": \"Alice\" }");
Net.DownloadFile("https://example.com/image.png", "image.png");
```

### Notes

- Headers should persist across requests within a session (set via `SetHeader`)
- `SetBaseUrl` allows relative URLs in subsequent calls
- Consider adding a `Net.Request` method for full control (custom method, headers, body)
- Could add async variants later: `Net.HttpGetAsync`
