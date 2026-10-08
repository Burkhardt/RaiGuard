using System;
using System.IO;

namespace HeatRecord
{
    public static class ProgramNaive
    {
        public static void Main(string[] args)
        {
            // Naive weather fetch and caching workflow with BCL anti-patterns
            string city = args.Length > 0 ? args[0] : "San Diego";
            string citySlug = city.Replace(" ", "");

            // Anti-pattern 1: Using System.IO.Path.Combine (RAI003)
            string weatherDir = Path.Combine(Directory.GetCurrentDirectory(), "Weather");
            string dailyHeatDir = Path.Combine(weatherDir, "DailyHeat-" + citySlug);

            // Anti-pattern 2: Using System.IO.Directory.CreateDirectory (RAI001)
            Directory.CreateDirectory(dailyHeatDir);

            // Anti-pattern 3: Using System.IO.File.WriteAllText for offline cache (RAI002)
            string cacheFile = Path.Combine(weatherDir, "DailyHeat-" + citySlug + "-OfflineSample.json");
            File.WriteAllText(cacheFile, "{\"city\":\"" + city + "\",\"status\":\"offline-cache\",\"oct08\":{\"2024\":31.5,\"2025\":33.2,\"2026\":34.8}}");

            // Anti-pattern 4: Using System.IO.File.ReadAllLines to read cached data (RAI002)
            string[] cachedLines = File.ReadAllLines(cacheFile);
            Console.WriteLine($"Read {cachedLines.Length} lines from cached dataset.");

            // Anti-pattern 3 & 4 again: Writing and reading manifest via File
            string manifestPath = Path.Combine(weatherDir, "DailyHeat-" + citySlug + "-Manifest.txt");
            File.WriteAllText(manifestPath, "DailyHeat Ingestion Manifest\nCity: " + city + "\nStatus: Complete");
            string[] manifestLines = File.ReadAllLines(manifestPath);
            Console.WriteLine($"Manifest lines: {manifestLines.Length}");
        }
    }
}
