using System.Diagnostics;
using System.Text;

namespace D4BuildFilter.Core;

/// <summary>
/// Browser-shaped HTTP GET for sites behind Cloudflare. Cloudflare bot-management fingerprints
/// the TLS ClientHello (JA3); .NET's HttpClient gets 403'd on some sites (e.g. mobalytics.gg) while
/// the system <c>curl.exe</c> — which ships in C:\Windows\System32 on Windows 10/11 — is allowed.
/// So we try curl first and fall back to HttpClient (which is fine for non-strict sites like maxroll).
/// </summary>
public static class BrowserFetch
{
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    public static async Task<string> GetStringAsync(string url, CancellationToken ct = default)
    {
        var viaCurl = await TryCurlAsync(url, ct);
        if (!string.IsNullOrEmpty(viaCurl)) return viaCurl!;
        return await HttpClientGetAsync(url, ct);
    }

    private static async Task<string?> TryCurlAsync(string url, CancellationToken ct)
    {
        var curl = FindCurl();
        if (curl is null) return null;
        try
        {
            var psi = new ProcessStartInfo(curl)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            foreach (var a in new[] { "-s", "-L", "--compressed", "--max-time", "30", "-A", UserAgent, url.Trim() })
                psi.ArgumentList.Add(a);

            using var proc = Process.Start(psi);
            if (proc is null) return null;
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            var stdout = await stdoutTask;
            return proc.ExitCode == 0 && stdout.Length > 0 ? stdout : null;
        }
        catch
        {
            return null;   // curl missing/blocked → fall back to HttpClient
        }
    }

    private static string? FindCurl()
    {
        var sys = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "curl.exe");
        return File.Exists(sys) ? sys : "curl";   // else hope it's on PATH; TryCurlAsync handles failure
    }

    private static async Task<string> HttpClientGetAsync(string url, CancellationToken ct)
    {
        using var http = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
        });
        using var req = new HttpRequestMessage(HttpMethod.Get, url.Trim())
        {
            Version = System.Net.HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
        var h = req.Headers;
        h.TryAddWithoutValidation("User-Agent", UserAgent);
        h.TryAddWithoutValidation("Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
        h.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        h.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
        h.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
        h.TryAddWithoutValidation("Sec-Fetch-Site", "none");
        h.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }
}
