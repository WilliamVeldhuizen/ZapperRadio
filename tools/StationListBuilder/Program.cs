using System.Net;
using System.Text;
using ZapperRadio.Core.Catalog;

// Downloads every station from radio-browser.info and writes the list the app reads into the folder given as the
// first argument: stations-yyyy-MM-dd.txt and the index.html that points at it.
//   dotnet run --project tools/StationListBuilder -- website/stations

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: StationListBuilder <output folder>");
    return 2;
}

// Fewer stations than this means radio-browser answered with part of its list; publishing that would take most
// stations away from every app until the next day.
const int MinimumStations = 20_000;

var output = args[0];
using var http = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All })
{
    Timeout = TimeSpan.FromMinutes(5),
};
// radio-browser.info asks its clients for a user agent that names them.
http.DefaultRequestHeaders.UserAgent.ParseAdd("ZapperRadio-StationListBuilder/1.0 (+https://zapperradio.com)");

var now = DateTimeOffset.UtcNow;
IReadOnlyList<ZapperRadio.Core.Models.Station>? stations = null;
foreach (var server in await FindServersAsync())
{
    try
    {
        Console.WriteLine($"Downloading the stations from {server}");
        var entries = new List<RadioBrowserStation>();
        while (true)
        {
            await using var json = await http.GetStreamAsync(new Uri(server, StationListBuilder.PageQuery(entries.Count)));
            var page = await StationListBuilder.ReadAsync(json);
            entries.AddRange(page);
            Console.WriteLine($"  {entries.Count:N0} stations");
            if (page.Count < StationListBuilder.PageSize)
            {
                break;
            }
        }

        stations = StationListBuilder.Build(entries, now);
        break;
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or IOException)
    {
        Console.WriteLine($"  failed: {ex.Message}");
    }
}

if (stations is null)
{
    Console.Error.WriteLine("No radio-browser.info server answered.");
    return 1;
}

if (stations.Count < MinimumStations)
{
    Console.Error.WriteLine($"Only {stations.Count} stations, fewer than the {MinimumStations} expected; not publishing it.");
    return 1;
}

Directory.CreateDirectory(output);
var generatedAt = now.UtcDateTime;
var fileName = StationListBuilder.FileName(generatedAt);
var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
await using (var writer = new StreamWriter(Path.Combine(output, fileName), append: false, utf8))
{
    StationListBuilder.Write(writer, generatedAt, stations);
}

await File.WriteAllTextAsync(Path.Combine(output, "index.html"), StationListBuilder.IndexHtml(fileName, generatedAt, stations.Count), utf8);
Console.WriteLine($"Wrote {stations.Count:N0} stations to {Path.Combine(output, fileName)}");
return 0;

// The servers as radio-browser.info asks clients to find them: the addresses behind all.api.radio-browser.info,
// each by its own name (the certificates are for the names), in random order to spread the load.
static async Task<IReadOnlyList<Uri>> FindServersAsync()
{
    var names = new List<string>();
    try
    {
        foreach (var address in await Dns.GetHostAddressesAsync("all.api.radio-browser.info"))
        {
            try
            {
                var host = (await Dns.GetHostEntryAsync(address)).HostName;
                if (host.EndsWith(".api.radio-browser.info", StringComparison.OrdinalIgnoreCase) && !names.Contains(host, StringComparer.OrdinalIgnoreCase))
                {
                    names.Add(host);
                }
            }
            catch (System.Net.Sockets.SocketException)
            {
                // An address without a name cannot be asked over https.
            }
        }
    }
    catch (System.Net.Sockets.SocketException)
    {
        // Falls back to the fixed servers below.
    }

    var servers = names.OrderBy(_ => Random.Shared.Next()).Select(name => new Uri($"https://{name}/")).ToList();
    servers.AddRange(StationPopularity.DefaultApiServers.Where(s => !servers.Contains(s)));
    return servers;
}
