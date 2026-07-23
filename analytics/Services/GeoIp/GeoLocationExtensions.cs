using AnalyticsApi.Contracts.Responses;

namespace AnalyticsApi.Services.GeoIp;

public static class GeoLocationExtensions
{
    public static GeoLocation MergeWith(this GeoLocation preferred, GeoLocation other) =>
        new(
            preferred.CountryCode ?? other.CountryCode,
            preferred.Region ?? other.Region,
            preferred.City ?? other.City,
            preferred.PostalCode ?? other.PostalCode,
            preferred.Latitude ?? other.Latitude,
            preferred.Longitude ?? other.Longitude,
            preferred.Asn ?? other.Asn,
            preferred.Org ?? other.Org,
            preferred.Isp ?? other.Isp);
}
