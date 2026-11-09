using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using XiaoYang.AutoCADPlugin;

internal static class CatalogSnapshotRegression
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static void Run(Action<bool, string> check, string output)
    {
        var catalog = new CadCommandCatalog();
        var cacheSnapshot = typeof(CadCommandCatalog).GetMethod("CacheSnapshot", Private);
        var cached = typeof(CadCommandCatalog).GetField("_cached", Private);
        var first = new[] { Command("LINE"), Command("0"), Command("01"), Command("-PURGE"), Command("VENDOR_TOOL") };
        cacheSnapshot.Invoke(catalog, new object[] { first });
        check(catalog.IsCommandDefined("  _.'line  ") && catalog.IsCommandDefined("line;"),
            "snapshot lookup retains normalized case-insensitive command names");
        check(catalog.IsCommandDefined("0") && catalog.IsCommandDefined("01")
              && catalog.IsCommandDefined("_-purge"),
            "snapshot lookup retains numeric and signed command identities");
        check(!catalog.IsCommandDefined("L") && !catalog.IsCommandDefined("MISSING")
              && !catalog.IsCommandDefined(null),
            "snapshot lookup does not invent aliases or missing commands");
        check(ReferenceEquals(catalog.GetAll(), first) && catalog.IsLoaded,
            "completed command snapshot is reused without a host read");

        var second = new[] { Command("CIRCLE"), Command("0") };
        cacheSnapshot.Invoke(catalog, new object[] { second });
        check(!catalog.IsCommandDefined("LINE") && catalog.IsCommandDefined("circle")
              && catalog.IsCommandDefined("0"),
            "publishing a refreshed snapshot removes stale command names");

        // Offline preview fixtures replace the snapshot directly. That path
        // must observe the same command set as a normal completed host refresh.
        cached.SetValue(catalog, first);
        check(catalog.IsCommandDefined("LINE") && !catalog.IsCommandDefined("CIRCLE"),
            "replaced offline snapshot cannot retain an earlier lookup index");
        var empty = Array.Empty<CadCommandInfo>();
        cacheSnapshot.Invoke(catalog, new object[] { empty });
        check(catalog.IsLoaded && !catalog.IsCommandDefined("LINE")
              && ReferenceEquals(catalog.GetAll(), empty),
            "loaded empty snapshot remains cached and clears prior commands");

        var snapshot = Enumerable.Range(0, 4000).Select(i => Command("BENCH_VENDOR_" + i)).ToArray();
        cacheSnapshot.Invoke(catalog, new object[] { snapshot });
        var queries = Enumerable.Range(0, 12000)
            .Select(i => i % 2 == 0 ? "BENCH_VENDOR_3999" : "BENCH_MISSING").ToArray();
        foreach (var query in queries.Take(1000)) catalog.IsCommandDefined(query);
        var timer = Stopwatch.StartNew();
        var referenceFound = 0;
        foreach (var query in queries)
            if (snapshot.Any(command => command.GlobalName.Equals(query, StringComparison.OrdinalIgnoreCase))) referenceFound++;
        timer.Stop();
        var referenceTicks = timer.ElapsedTicks;
        timer.Restart();
        var indexedFound = 0;
        foreach (var query in queries)
            if (catalog.IsCommandDefined(query)) indexedFound++;
        timer.Stop();
        check(referenceFound == indexedFound && indexedFound == queries.Length / 2,
            "indexed lookups match the previous linear search for large snapshots");
        Directory.CreateDirectory(output);
        var report = "commands,lookups,linear_reference_ms,indexed_ms\n"
                     + snapshot.Length + "," + queries.Length + ","
                     + (referenceTicks * 1000d / Stopwatch.Frequency).ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + ","
                     + timer.Elapsed.TotalMilliseconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "\n";
        File.WriteAllText(Path.Combine(output, "catalog-lookup-timing.csv"), report);
        Console.WriteLine(report.Trim());
    }

    private static CadCommandInfo Command(string name) => new() { GlobalName = name, DisplayName = name };
}
