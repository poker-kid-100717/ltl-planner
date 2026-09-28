namespace Portfolio.Ltl.Api.Integrations.Alvys;

public sealed class AlvysOptions
{
    public const string Section = "Alvys";
    public string Mode { get; set; } = "Demo";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string TokenUrl { get; set; } = "https://auth.alvys.com/oauth/token";
    public string Audience { get; set; } = "https://api.alvys.com/public/";
    public string ApiBaseUrl { get; set; } = "https://integrations.alvys.com/api/p/";
    public string LoadApiVersion { get; set; } = "v1";
    public bool LiveConfigured => Mode.Equals("Live", StringComparison.OrdinalIgnoreCase) && ClientId.Length > 0 && ClientSecret.Length > 0;
}
