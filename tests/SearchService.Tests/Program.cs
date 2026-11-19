using System.Diagnostics;
using BlockBox.SearchService;
using Microsoft.Data.Sqlite;

if (args.Length == 2 && args[0] == "--bench") return Bench(args[1]);

int failures = 0;
void Check(bool condition, string message)
{
    if (condition) Console.WriteLine("PASS  " + message);
    else { failures++; Console.WriteLine("FAIL  " + message); }
}

// ---------------------------------------------------------------- ordering, paths, filters
var catalog = new SearchCatalog();
catalog.BeginVolume("V1", @"C:\");
catalog.AddBatch("V1", new MftEntry[]
{
    new(5, 5, ".", true),
    new(100, 5, "Windows", true),
    new(101, 100, "System32", true),
    new(200, 101, "notepad.exe", false),
    new(201, 101, "notepad.txt", false),
    new(202, 101, "NOTEPAD.EXE", false),
    new(203, 5, "photoshop.exe", false)
});
catalog.SetState("V1", "Ready");

var notepad = catalog.Search("notepad", "all", 10, null, null, null);
Check(notepad.Count == 3, "finds three notepad names case-insensitively");
Check(notepad[0].FileId == 200 && notepad[0].Name == "notepad.exe", "exact name sorts before other names");
Check(notepad[1].FileId == 202 && notepad[1].Name == "NOTEPAD.EXE", "case-different duplicate follows by file id");
Check(notepad[2].FileId == 201 && notepad[2].Name == "notepad.txt", "remaining exact matches sort by folded name");
Check(notepad[0].Path == @"C:\Windows\System32\notepad.exe", "path is rebuilt from parent references");
Check(catalog.Search("NOTEPAD", "all", 10, null, null, null).Count == 3, "query case is ignored");
Check(catalog.Search("notepad", "application", 10, null, null, null).Count == 2, "application filter keeps executables");
Check(catalog.Search("notepad", "image", 10, null, null, null).Count == 0, "image filter drops executables");
Check(catalog.Search("wind", "all", 10, null, null, null).Single().IsDirectory, "directories are searchable");

var firstPage = catalog.Search("notepad", "all", 1, null, null, null);
Check(firstPage.Count == 2, "a limit of one still reports the extra row used for hasMore");
var secondPage = catalog.Search("notepad", "all", 1, firstPage[0].Name, firstPage[0].VolumeId, firstPage[0].FileId);
var thirdPage = catalog.Search("notepad", "all", 1, secondPage[0].Name, secondPage[0].VolumeId, secondPage[0].FileId);
var fourthPage = catalog.Search("notepad", "all", 1, thirdPage[0].Name, thirdPage[0].VolumeId, thirdPage[0].FileId);
Check(secondPage[0].FileId == 202 && thirdPage[0].FileId == 201 && fourthPage.Count == 0,
    "cursor pagination walks every result exactly once");

// ---------------------------------------------------------------- journal updates
catalog.ApplyJournal("V1", new JournalReadResult(0, new JournalEntry[]
{
    new(201, 101, 1, 0x00000200u, "notepad.txt", false),
    new(200, 101, 2, 0x00001000u, "notepad.exe", false),
    new(200, 101, 3, 0x00002000u, "notepad2.exe", false)
}));
var renamed = catalog.Search("notepad", "all", 10, null, null, null);
Check(renamed.Count == 2 && renamed.Any(h => h.Name == "notepad2.exe") && renamed.All(h => h.Name != "notepad.txt"),
    "journal delete and rename apply to the in-memory index");
Check(renamed.Single(h => h.Name == "notepad2.exe").Path == @"C:\Windows\System32\notepad2.exe",
    "renamed entry keeps its parent chain");

// ---------------------------------------------------------------- hard links
catalog.BeginVolume("V2", @"D:\");
catalog.AddBatch("V2", new MftEntry[]
{
    new(5, 5, ".", true),
    new(300, 5, "shared.a.txt", false),
    new(300, 5, "shared.b.txt", false)
});
catalog.SetState("V2", "Ready");
Check(catalog.Search("shared", "all", 10, null, null, null).Count == 2, "both hard-link names are indexed");
catalog.ApplyJournal("V2", new JournalReadResult(0, new[] { new JournalEntry(300, 5, 1, 0x00000200u, "shared.a.txt", false) }));
var remainingLinks = catalog.Search("shared", "all", 10, null, null, null);
Check(remainingLinks.Count == 1 && remainingLinks[0].Name == "shared.b.txt", "deleting one hard-link name keeps the other");

// ---------------------------------------------------------------- non-ASCII names
catalog.BeginVolume("V3", @"E:\");
catalog.AddBatch("V3", new MftEntry[]
{
    new(5, 5, ".", true),
    new(400, 5, "年度报告-2026.docx", false),
    new(401, 5, "资料", true)
});
catalog.SetState("V3", "Ready");
Check(catalog.Search("报告", "all", 10, null, null, null).Single().Name == "年度报告-2026.docx", "Chinese substring matches");
Check(catalog.Search("资料", "all", 10, null, null, null).Single().Path == @"E:\资料", "Chinese directory path is built");
Check(catalog.Search("2026.DOCX", "all", 10, null, null, null).Count == 1, "ASCII case folding works next to Chinese text");

// ---------------------------------------------------------------- empty query lists files
var listing = catalog.Search("", "all", 100, null, null, null);
Check(listing.Count == 5, "empty query lists every file");
Check(listing.All(h => !h.IsDirectory), "empty query hides directories");
Check(listing.All(h => h.Name != "."), "empty query hides the volume root record");
Check(listing.Select(h => h.Name).SequenceEqual(
        listing.Select(h => h.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)),
    "empty query is ordered by name");
Check(catalog.Search("", "application", 10, null, null, null).Count == 3, "empty query still honours the kind filter");
Check(catalog.Search("", "image", 10, null, null, null).Count == 0, "empty query with a kind that has no files");
Check(catalog.Search("", "all", 2, null, null, null).Count == 3, "empty query reports the row used for hasMore");

var listingPage = catalog.Search("", "all", 2, null, null, null);
var listingNext = catalog.Search("", "all", 2, listingPage[1].Name, listingPage[1].VolumeId, listingPage[1].FileId);
Check(listingNext.Count == 3 && listingNext[0].Name == listing[2].Name, "empty query pagination continues in name order");

// ---------------------------------------------------------------- archive kind
catalog.BeginVolume("V4", @"H:\");
catalog.AddBatch("V4", new MftEntry[]
{
    new(5, 5, ".", true),
    new(600, 5, "backup-2026.zip", false),
    new(601, 5, "photos.rar", false),
    new(602, 5, "linux.tar", false),
    new(603, 5, "notes.txt", false),
    new(604, 5, "plain", false),
    new(605, 5, "archives", true)
});
catalog.SetState("V4", "Ready");
Check(catalog.Search("", "archive", 10, null, null, null).Count == 3, "archive kind lists only archives");
Check(catalog.Search("photos", "archive", 10, null, null, null).Single().Name == "photos.rar", "archive kind filters a query");
Check(catalog.Search("", "video", 10, null, null, null).Count == 0, "archives are not reported as other kinds");



// ---------------------------------------------------------------- paged vs single-shot on many rows
var bulk = new SearchCatalog();
bulk.BeginVolume("BULK", @"F:\");
var batch = new List<MftEntry>(2048);
for (int i = 0; i < 25_000; i++)
{
    string name = i % 100 == 0 ? $"FILE{i:D5}.DAT" : $"file{i:D5}.dat";
    batch.Add(new MftEntry(1_000_000 + i, 5, name, false));
    if (batch.Count == 2048) { bulk.AddBatch("BULK", batch); batch.Clear(); }
}
if (batch.Count > 0) bulk.AddBatch("BULK", batch);
bulk.SetState("BULK", "Ready");

var all = bulk.Search("file", "all", 100_000, null, null, null);
Check(all.Count == 25_000, "every generated row matches");
var paged = new List<SearchHit>();
string? afterName = null, afterVolume = null;
long? afterFile = null;
while (true)
{
    var page = bulk.Search("file", "all", 500, afterName, afterVolume, afterFile);
    bool hasMore = page.Count > 500;
    var rows = hasMore ? page.Take(500).ToList() : page;
    paged.AddRange(rows);
    if (!hasMore) break;
    var last = rows[^1];
    afterName = last.Name; afterVolume = last.VolumeId; afterFile = last.FileId;
}
Check(paged.Count == all.Count, "paging returns every row");
Check(paged.Select(h => h.FileId).SequenceEqual(all.Select(h => h.FileId)), "paging preserves the exact global order");

// ---------------------------------------------------------------- descending order
var descNotepad = catalog.Search("notepad", "all", 10, null, null, null, descending: true);
Check(descNotepad.Count == 2 && descNotepad[0].Name == "NOTEPAD.EXE",
    "descending still keeps exact matches first");

var descendingAll = bulk.Search("file", "all", 100_000, null, null, null, descending: true);
Check(descendingAll.Count == all.Count, "descending returns every row");
Check(descendingAll.Select(h => h.FileId).SequenceEqual(all.Select(h => h.FileId).Reverse()),
    "descending is the exact reverse of ascending");

var descendingPaged = new List<SearchHit>();
string? descName = null, descVolume = null;
long? descFile = null;
while (true)
{
    var page = bulk.Search("file", "all", 500, descName, descVolume, descFile, descending: true);
    bool hasMore = page.Count > 500;
    var rows = hasMore ? page.Take(500).ToList() : page;
    descendingPaged.AddRange(rows);
    if (!hasMore) break;
    var last = rows[^1];
    descName = last.Name; descVolume = last.VolumeId; descFile = last.FileId;
}
Check(descendingPaged.Select(h => h.FileId).SequenceEqual(descendingAll.Select(h => h.FileId)),
    "descending paging keeps the exact order");

var bulkListing = bulk.Search("", "all", 100_000, null, null, null);
Check(bulkListing.Count == 25_000, "empty query lists the whole bulk volume");
Check(bulk.Search("", "application", 10, null, null, null).Count == 0, "empty query kind filter excludes other extensions");

// ---------------------------------------------------------------- scale
var big = new SearchCatalog();
big.BeginVolume("BIG", @"G:\");
var sw = Stopwatch.StartNew();
var bigBatch = new List<MftEntry>(4096);
for (int i = 0; i < 1_000_000; i++)
{
    string name = (i % 3) switch
    {
        0 => $"chrome-{i:D7}.dll",
        1 => $"photo-{i:D7}.jpg",
        _ => $"report-{i:D7}.docx"
    };
    bigBatch.Add(new MftEntry(2_000_000 + i, 5, name, false));
    if (bigBatch.Count == 4096) { big.AddBatch("BIG", bigBatch); bigBatch.Clear(); }
}
if (bigBatch.Count > 0) big.AddBatch("BIG", bigBatch);
big.SetState("BIG", "Ready");
sw.Stop();
Console.WriteLine($"INFO  indexed 1,000,000 names in {sw.ElapsedMilliseconds} ms, managed memory {GC.GetTotalMemory(false) / (1024 * 1024)} MB");

for (int round = 0; round < 2; round++)
{
    sw.Restart();
    var none = big.Search("zzzz-not-there", "all", 100, null, null, null);
    long scanOnly = sw.ElapsedMilliseconds;
    sw.Restart();
    var narrow = big.Search("chrome-0004242.dll", "all", 100, null, null, null);
    long narrowMs = sw.ElapsedMilliseconds;
    sw.Restart();
    var broad = big.Search("chrome-", "all", 100, null, null, null);
    long broadMs = sw.ElapsedMilliseconds;
    if (round == 1)
    {
        Console.WriteLine($"INFO  scan-only {scanOnly} ms, narrow {narrowMs} ms, broad ({broad.Count} rows) {broadMs} ms");
        Check(none.Count == 0 && narrow.Count == 1 && broad.Count > 100, "1M-row queries return the right rows");
        Check(scanOnly < 2000, "a full 1M-name scan stays under two seconds");
        Check(broadMs < 5000, "a broad 1M-match query stays under five seconds");
    }
}

// ---------------------------------------------------------------- sort keys
{
    var sorted = new VolumeIndex("V:", @"C:\", "Ready", 8, 64);
    int small = sorted.Upsert(11, 5, "small.bin", false);
    int large = sorted.Upsert(12, 5, "big.bin", false);
    int unknown = sorted.Upsert(13, 5, "unknown.bin", false);
    sorted.SetMetadata(small, 1_000, 1000);
    sorted.SetMetadata(large, 900_000, 2000);
    Check(VolumeIndex.CompareEntries(sorted, large, 0, sorted, small, 0, SearchSort.Size, false) < 0,
        "size sort puts the bigger file first");
    Check(VolumeIndex.CompareEntries(sorted, small, 0, sorted, large, 0, SearchSort.Size, true) < 0,
        "reversed size sort puts the smaller file first");
    Check(VolumeIndex.CompareEntries(sorted, unknown, 0, sorted, large, 0, SearchSort.Size, false) > 0,
        "unknown size sorts last");
    Check(VolumeIndex.CompareEntries(sorted, large, 0, sorted, small, 0, SearchSort.Time, false) < 0,
        "time sort puts the newer file first");
    Check(VolumeIndex.CompareEntries(sorted, small, 0, sorted, large, 0, SearchSort.Time, true) < 0,
        "reversed time sort puts the older file first");
}

// ---------------------------------------------------------------- long→int map
var map = new LongIntMap(4);
for (int i = 0; i < 100_000; i++) map.Set(i * 7L - 50_000, i);
bool mapOk = map.Count == 100_000;
for (int i = 0; i < 100_000 && mapOk; i++)
    mapOk = map.TryGetValue(i * 7L - 50_000, out int stored) && stored == i;
Check(mapOk, "LongIntMap stores and returns 100k entries");
map.Set(-123, 7);
map.Set(-123, 9);
Check(map.TryGetValue(-123, out int overwritten) && overwritten == 9 && map.Count == 100_001,
    "LongIntMap overwrites existing keys without growing");
Check(!map.TryGetValue(999_999_999, out _), "LongIntMap reports missing keys");

// ---------------------------------------------------------------- durable snapshot round trip
string storeDirectory = Path.Combine(Path.GetTempPath(), "bb-search-store-" + Guid.NewGuid().ToString("N"));
string storePath = Path.Combine(storeDirectory, "search-index.db");
string volumeId = @"\\?\Volume{11111111-2222-3333-4444-555555555555}\";
var store = new SearchIndexStore(storePath);
store.BeginVolume(volumeId, @"C:\", 0, 0);
store.AddBatch(volumeId, new MftEntry[]
{
    new(5, 5, ".", true),
    new(500, 5, "Tools", true),
    new(501, 500, "editor.exe", false),
    new(502, 500, "editor.ini", false)
});
store.CompleteVolume(volumeId, 0, 100);
Check(store.SearchNames("editor", "all", 10).Count == 2, "store answers from memory after the initial scan");
Check(store.GetStatus() == ("ready", 1, 1), "store reports one ready volume");

var reopened = new SearchIndexStore(storePath);
Check(reopened.SearchNames("editor", "all", 10).Count == 2, "reopened store reloads names from the snapshot");
Check(reopened.SearchNames("editor", "all", 10)[0].Path == @"C:\Tools\editor.exe", "reloaded rows keep their path");

reopened.ApplyJournalBatch(volumeId, 0, new JournalReadResult(120, new JournalEntry[]
{
    new(502, 500, 110, 0x00000200u, "editor.ini", false)
}), markReady: true);
Check(reopened.SearchNames("editor", "all", 10).Count == 1, "journal delete reaches memory through the store");

var reopenedAgain = new SearchIndexStore(storePath);
Check(reopenedAgain.SearchNames("editor", "all", 10).Count == 1, "journal delete was persisted");

Console.WriteLine(failures == 0 ? "ALL TESTS PASSED" : $"{failures} TEST(S) FAILED");
return failures == 0 ? 0 : 1;

static int Bench(string path)
{
    using (var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly"))
    {
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT COUNT(*) FROM Entries),(SELECT COUNT(*) FROM sqlite_master WHERE name='EntryNameFts')";
        using var reader = command.ExecuteReader();
        reader.Read();
        Console.WriteLine($"rows={reader.GetInt64(0)} legacyFts={reader.GetInt64(1)} dbMB={new FileInfo(path).Length / (1024 * 1024)}");
    }

    var watch = Stopwatch.StartNew();
    var store = new SearchIndexStore(path);
    watch.Stop();
    Console.WriteLine($"open+load: {watch.ElapsedMilliseconds} ms, heap {GC.GetTotalMemory(false) / (1024 * 1024)} MB");
    Console.WriteLine($"status: {store.GetStatus()}");

    foreach (string query in new[] { "", "chrome", "notepad", "报告", "setup", "a", "e", "zzzz" })
    {
        store.SearchNames(query, "all", 500, null, null, null);
        watch.Restart();
        var rows = store.SearchNames(query, "all", 500, null, null, null);
        watch.Stop();
        Console.WriteLine($"query '{query}': {watch.ElapsedMilliseconds} ms, rows={rows.Count}");
    }

    watch.Restart();
    var applications = store.SearchNames("", "application", 500, null, null, null);
    watch.Stop();
    Console.WriteLine($"empty query, application kind: {watch.ElapsedMilliseconds} ms, rows={applications.Count}");

    var first = store.SearchNames("e", "all", 500, null, null, null);
    var last = first[^1];
    watch.Restart();
    var page2 = store.SearchNames("e", "all", 500, last.Name, last.VolumeId, last.FileId);
    watch.Stop();
    Console.WriteLine($"page 2 of 'e': {watch.ElapsedMilliseconds} ms, rows={page2.Count}");

    watch.Restart();
    var reopened = new SearchIndexStore(path);
    watch.Stop();
    store = null!;
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    Console.WriteLine($"reopen: {watch.ElapsedMilliseconds} ms, heap after GC {GC.GetTotalMemory(true) / (1024 * 1024)} MB");
    Console.WriteLine($"reopened status: {reopened.GetStatus()}");
    return 0;
}
