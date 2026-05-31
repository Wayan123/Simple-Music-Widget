using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MusicWidget;

/// <summary>Lightweight JSON persistence for YouTube search queries and played tracks.</summary>
public static class HistoryStore
{
    private const int Max = 50;
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MusicWidget");
    private static readonly string SearchFile = Path.Combine(Dir, "searches.json");
    private static readonly string PlayedFile = Path.Combine(Dir, "played.json");

    public static List<string> Searches { get; private set; } = Load<List<string>>(SearchFile) ?? new();
    public static List<YtResult> Played { get; private set; } = Load<List<YtResult>>(PlayedFile) ?? new();

    public static void AddSearch(string query)
    {
        query = query.Trim();
        if (query.Length == 0) return;
        Searches.RemoveAll(s => s.Equals(query, StringComparison.OrdinalIgnoreCase));
        Searches.Insert(0, query);
        if (Searches.Count > Max) Searches.RemoveRange(Max, Searches.Count - Max);
        Save(SearchFile, Searches);
    }

    public static void AddPlayed(YtResult track)
    {
        Played.RemoveAll(t => t.Id == track.Id);
        Played.Insert(0, track);
        if (Played.Count > Max) Played.RemoveRange(Max, Played.Count - Max);
        Save(PlayedFile, Played);
    }

    public static void RemoveSearch(string query)
    {
        Searches.RemoveAll(s => s.Equals(query, StringComparison.OrdinalIgnoreCase));
        Save(SearchFile, Searches);
    }

    public static void RemovePlayed(string id)
    {
        Played.RemoveAll(t => t.Id == id);
        Save(PlayedFile, Played);
    }

    public static IEnumerable<string> MatchSearches(string prefix) =>
        Searches.Where(s => s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static T? Load<T>(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) : default; }
        catch { return default; }
    }

    private static void Save<T>(string path, T data)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(path, JsonSerializer.Serialize(data));
        }
        catch { }
    }
}
