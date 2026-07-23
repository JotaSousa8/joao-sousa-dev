using System.Text;

namespace AnalyticsApi.Infrastructure;

public static class ConnectionStringResolver
{
    public static (string Value, string Source) ResolveDetailed(IConfiguration config)
    {
        static string? NullIfEmpty(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        // Prefer direct process env (ACA secretref) before layered config.
        var b64 =
            NullIfEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__AnalyticsB64"))
            ?? NullIfEmpty(config["ConnectionStrings:AnalyticsB64"])
            ?? NullIfEmpty(config["ANALYTICS_CONNECTION_STRING_B64"])
            ?? NullIfEmpty(Environment.GetEnvironmentVariable("ANALYTICS_CONNECTION_STRING_B64"));
        if (b64 is not null)
        {
            try
            {
                return (Normalize(Encoding.UTF8.GetString(Convert.FromBase64String(b64))), "b64");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("ANALYTICS_CONNECTION_STRING_B64 is not valid base64.", ex);
            }
        }

        var raw =
            NullIfEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__Analytics"))
            ?? NullIfEmpty(Environment.GetEnvironmentVariable("ANALYTICS_CONNECTION_STRING"))
            ?? NullIfEmpty(config.GetConnectionString("Analytics"))
            ?? NullIfEmpty(config["Analytics:ConnectionString"])
            ?? NullIfEmpty(config["ANALYTICS_CONNECTION_STRING"])
            ?? NullIfEmpty(config["DATABASE_URL"])
            ?? "";

        return raw.Length == 0 ? ("", "none") : (Normalize(raw), "plain");
    }

    public static string Normalize(string raw)
    {
        raw = raw.Trim().Trim('"').Trim('\'');

        // Supabase URI → Npgsql key=value (manual parse so passwords with '.' don't break ports)
        if (raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            var schemeSplit = raw.Split(["://"], 2, StringSplitOptions.None);
            if (schemeSplit.Length != 2)
            {
                throw new InvalidOperationException("Invalid Postgres URI.");
            }

            var rest = schemeSplit[1];
            var slash = rest.IndexOf('/');
            var netloc = slash >= 0 ? rest[..slash] : rest;
            var database = slash >= 0 ? rest[(slash + 1)..] : "postgres";
            if (string.IsNullOrWhiteSpace(database))
            {
                database = "postgres";
            }

            var at = netloc.LastIndexOf('@');
            if (at < 0)
            {
                throw new InvalidOperationException(
                    "Postgres URI missing USER:PASSWORD@HOST. Expected postgresql://postgres.PROJECT:PASSWORD@HOST:5432/postgres");
            }

            var userInfo = netloc[..at];
            var hostPort = netloc[(at + 1)..];
            var colon = userInfo.IndexOf(':');
            if (colon < 0)
            {
                throw new InvalidOperationException(
                    "Postgres URI missing :PASSWORD before @HOST (password was probably put in the port field).");
            }

            var user = Uri.UnescapeDataString(userInfo[..colon]);
            var password = Uri.UnescapeDataString(userInfo[(colon + 1)..]);

            string host;
            var port = 5432;
            var portColon = hostPort.LastIndexOf(':');
            if (portColon > 0)
            {
                host = hostPort[..portColon];
                var portText = hostPort[(portColon + 1)..];
                if (!int.TryParse(portText, out port))
                {
                    throw new InvalidOperationException(
                        $"Invalid port '{portText}' in Postgres URI — use :5432 after the host.");
                }
            }
            else
            {
                host = hostPort;
            }

            return $"Host={host};Port={port};Database={database};Username={user};Password={password};SSL Mode=Require;Trust Server Certificate=true";
        }

        if (!raw.Contains("SSL Mode", StringComparison.OrdinalIgnoreCase)
            && !raw.Contains("Ssl Mode", StringComparison.OrdinalIgnoreCase)
            && !raw.Contains("sslmode", StringComparison.OrdinalIgnoreCase))
        {
            raw += raw.EndsWith(';')
                ? "SSL Mode=Require;Trust Server Certificate=true"
                : ";SSL Mode=Require;Trust Server Certificate=true";
        }

        return raw;
    }
}
