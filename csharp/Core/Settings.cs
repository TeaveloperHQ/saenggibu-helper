using System.Text.Json;

namespace Saenggibu;

/// <summary>app/settings.py 이식 — 사용자별 영속 설정(JSON key-value).</summary>
public sealed class Settings
{
    private readonly string _path;
    public Settings(string dataDir) => _path = Path.Combine(dataDir, "settings.json");

    public Dictionary<string, JsonElement> Load()
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(_path))
                   ?? new();
        }
        catch (Exception e) when (e is IOException or JsonException) { return new(); }
    }

    public T? Get<T>(string key, T? fallback = default)
    {
        var d = Load();
        if (d.TryGetValue(key, out var el))
            try { return el.Deserialize<T>(); } catch (JsonException) { }
        return fallback;
    }

    public void Set<T>(string key, T value)
    {
        var d = Load();
        d[key] = JsonSerializer.SerializeToElement(value);
        var opts = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        File.WriteAllText(_path, JsonSerializer.Serialize(d, opts));
    }
}
