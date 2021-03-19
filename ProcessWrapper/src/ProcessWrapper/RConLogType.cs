using System.Text.Json.Serialization;

namespace ProcessWrapper
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RConLogType
    {
        Generic,
        Error,
        Warning,
        Chat,
        Report
    }
}
