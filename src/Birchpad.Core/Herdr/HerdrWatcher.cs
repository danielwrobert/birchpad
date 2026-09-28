namespace Birchpad.Core.Herdr;

/// <summary>
/// Polls herdr's session snapshot and raises <see cref="Changed"/> when anything a key shows changes.
/// Polls every second while herdr runs and every five seconds while it does not.
/// </summary>
public sealed class HerdrWatcher : IDisposable
{
    private static readonly TimeSpan Fast = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Slow = TimeSpan.FromSeconds(5);

    private readonly HerdrClient client;
    private readonly Timer timer;
    private string fingerprint = string.Empty;
    private bool disposed;

    public HerdrWatcher(HerdrClient client)
    {
        this.client = client;
        this.timer = new Timer(_ => this.Poll(), null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
    }

    public event EventHandler? Changed;

    public HerdrSnapshot Latest { get; private set; } = HerdrSnapshot.Empty;

    /// <summary>Reads the snapshot now, for example right after a key press changed focus.</summary>
    public void Poke()
    {
        lock (this.timer)
        {
            if (!this.disposed)
            {
                this.timer.Change(TimeSpan.FromMilliseconds(150), Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void Poll()
    {
        var snapshot = this.client.Snapshot();
        var next = snapshot.Fingerprint();
        this.Latest = snapshot;
        if (next != this.fingerprint)
        {
            this.fingerprint = next;
            this.Changed?.Invoke(this, EventArgs.Empty);
        }

        lock (this.timer)
        {
            // The next poll starts only after this one ends, so slow replies never pile up.
            if (!this.disposed)
            {
                this.timer.Change(ReferenceEquals(snapshot, HerdrSnapshot.Empty) ? Slow : Fast, Timeout.InfiniteTimeSpan);
            }
        }
    }

    public void Dispose()
    {
        lock (this.timer)
        {
            this.disposed = true;
            this.timer.Dispose();
        }
    }
}
