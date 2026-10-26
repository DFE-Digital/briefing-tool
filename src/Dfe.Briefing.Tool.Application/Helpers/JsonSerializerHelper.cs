using System.Text.Encodings.Web;
using System.Text.Json;

namespace Dfe.Briefing.Tool.Application.Helpers;

public static class JsonSerializerHelper
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
