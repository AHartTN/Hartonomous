using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Text;
using System.Text.Json;

namespace Hartonomous.API.Configuration;

/// <summary>
/// Custom response writer for health check endpoints to provide detailed JSON output.
/// </summary>
public static class HealthCheckResponseWriter
{
    /// <summary>
    /// Writes a detailed JSON response for health check results.
    /// </summary>
    public static Task WriteDetailedResponse(HttpContext context, HealthReport healthReport)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var options = new JsonWriterOptions { Indented = true };

        using var memoryStream = new MemoryStream();
        using (var jsonWriter = new Utf8JsonWriter(memoryStream, options))
        {
            jsonWriter.WriteStartObject();
            jsonWriter.WriteString("status", healthReport.Status.ToString());
            jsonWriter.WriteString("totalDuration", healthReport.TotalDuration.ToString());
            jsonWriter.WriteStartObject("results");

            foreach (var healthReportEntry in healthReport.Entries)
            {
                jsonWriter.WriteStartObject(healthReportEntry.Key);
                jsonWriter.WriteString("status", healthReportEntry.Value.Status.ToString());
                
                if (!string.IsNullOrEmpty(healthReportEntry.Value.Description))
                {
                    jsonWriter.WriteString("description", healthReportEntry.Value.Description);
                }

                jsonWriter.WriteString("duration", healthReportEntry.Value.Duration.ToString());

                if (healthReportEntry.Value.Exception != null)
                {
                    jsonWriter.WriteString("exception", healthReportEntry.Value.Exception.Message);
                }

                jsonWriter.WriteStartObject("data");

                foreach (var item in healthReportEntry.Value.Data)
                {
                    jsonWriter.WritePropertyName(item.Key);

                    JsonSerializer.Serialize(jsonWriter, item.Value,
                        item.Value?.GetType() ?? typeof(object));
                }

                jsonWriter.WriteEndObject();
                jsonWriter.WriteEndObject();
            }

            jsonWriter.WriteEndObject();
            jsonWriter.WriteEndObject();
        }

        return context.Response.WriteAsync(
            Encoding.UTF8.GetString(memoryStream.ToArray()));
    }
}
