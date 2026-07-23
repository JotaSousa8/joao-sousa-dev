namespace AnalyticsApi.Services;

public static class ExcludedIpOptions
{
    public static HashSet<string> Resolve(IConfiguration config)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ip in config.GetSection("Analytics:ExcludedIps").Get<string[]>() ?? [])
        {
            if (!string.IsNullOrWhiteSpace(ip))
            {
                set.Add(ip.Trim());
            }
        }

        var csv = config["ANALYTICS_EXCLUDED_IPS"]
            ?? Environment.GetEnvironmentVariable("ANALYTICS_EXCLUDED_IPS");
        if (!string.IsNullOrWhiteSpace(csv))
        {
            foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                set.Add(part);
            }
        }

        return set;
    }

    public static string LabelFor(string ip) => ip switch
    {
        "104.30.177.192" => "Work laptop",
        "176.79.88.124" => "Personal mobile",
        _ => "Own IP"
    };
}
