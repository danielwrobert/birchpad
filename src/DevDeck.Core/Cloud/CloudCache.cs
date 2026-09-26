using System.Text.Json;

namespace DevDeck.Core.Cloud;

/// <summary>
/// Runs the cloud helper on a timer and keeps the latest result in memory and on disk,
/// so tiles have data straight after a plugin reload. Readers never wait for a run.
/// </summary>
public sealed class CloudCache : IDisposable
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly Func<DateTimeOffset, CloudSnapshot> run;
    private readonly string cacheFile;
    private readonly Func<DateTimeOffset> clock;
    private readonly Timer timer;
    private int running;

    public CloudCache(Func<DateTimeOffset, CloudSnapshot> run, string cacheFile, TimeSpan interval, Func<DateTimeOffset>? clock = null)
    {
        this.run = run;
        this.cacheFile = cacheFile;
        this.Interval = interval;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        this.Latest = Load(cacheFile);
        this.timer = new Timer(_ => this.RefreshNow(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Raised after each run, successful or not.</summary>
    public event EventHandler? Changed;

    public TimeSpan Interval { get; }

    /// <summary>The last result, or null before the first run.</summary>
    public CloudSnapshot? Latest { get; private set; }

    public bool IsRunning => Volatile.Read(ref this.running) == 1;

    /// <summary>Starts the timer. The first run waits for the interval unless the cache is missing or stale.</summary>
    public void Start()
    {
        var age = this.Latest is null ? TimeSpan.MaxValue : this.clock() - this.Latest.FetchedAt;
        var due = age >= this.Interval ? TimeSpan.FromSeconds(5) : this.Interval - age;
        this.timer.Change(due, this.Interval);
    }

    /// <summary>Runs the helper now unless a run is already going. Returns false when one is.</summary>
    public bool RefreshNow()
    {
        if (Interlocked.CompareExchange(ref this.running, 1, 0) != 0)
        {
            return false;
        }

        this.Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            var result = this.run(this.clock());

            // Keep the last good lists when a run fails, and record the failure on top of them.
            this.Latest = result.Error is null || this.Latest is null
                ? result
                : this.Latest with { Error = result.Error };
            if (result.Error is null)
            {
                this.Save(result);
            }
        }
        finally
        {
            Volatile.Write(ref this.running, 0);
            this.Changed?.Invoke(this, EventArgs.Empty);
        }

        return true;
    }

    public void Dispose() => this.timer.Dispose();

    private void Save(CloudSnapshot snapshot)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(this.cacheFile)!);
            File.WriteAllText(this.cacheFile, JsonSerializer.Serialize(snapshot, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A cache that cannot be written only costs a run after the next reload.
        }
    }

    private static CloudSnapshot? Load(string file)
    {
        try
        {
            return File.Exists(file) ? JsonSerializer.Deserialize<CloudSnapshot>(File.ReadAllText(file)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }
}
