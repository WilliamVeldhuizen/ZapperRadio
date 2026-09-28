namespace ZapperRadio.Core.Models;

/// <summary>A radio station as listed in the station list (see <see cref="Catalog.StationListBuilder"/>).</summary>
public sealed record Station(string Name, string Tags, string Country, string Language, string Url)
{
    /// <summary>Short description for display, e.g. "Netherlands · pop, hits".</summary>
    public string Subtitle => string.Join(" · ", new[] { Country, Tags }.Where(s => !string.IsNullOrWhiteSpace(s)));
}
