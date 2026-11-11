using System.Security.Cryptography;
using RabbidLab.Shared;

namespace RabbidLab.Server;

/// <summary>
/// The server's way to ask a client to do something (only the account changes in AccountOrder, nothing else). Each
/// client keeps one request open to /api/clients/next; an order goes down it straight away, and the client sends how it
/// went to /api/clients/done. Orders live only in memory, passwords and all, and are gone once answered or given up on.
/// </summary>
public sealed class ClientCommands
{
    /// <summary>How long a client's request is held open when there's nothing for it.</summary>
    public static readonly TimeSpan Hold = TimeSpan.FromSeconds(50);

    /// <summary>How long the page waits for a client to answer.</summary>
    public static readonly TimeSpan Answer = TimeSpan.FromSeconds(40);

    private readonly Lock gate = new();
    private readonly Dictionary<string, Lane> lanes = [];
    private readonly Dictionary<string, Job> taken = [];

    private sealed class Lane
    {
        public List<Job> Jobs { get; } = [];
        public TaskCompletionSource? Bell { get; set; }
        public DateTime Asked { get; set; }
        public int Open { get; set; }

        /// <summary>The client was removed: its open requests end and it gets no more orders.</summary>
        public bool Gone { get; set; }
    }

    private sealed record Job(string Client, AccountOrder Order, TaskCompletionSource<AccountAnswer> Done);

    /// <summary>Whether the client is listening: it has a request open now, or had one a moment ago.</summary>
    public bool Listening(string client)
    {
        lock (gate)
        {
            return lanes.TryGetValue(client, out var lane) && (lane.Open > 0 || DateTime.UtcNow - lane.Asked < TimeSpan.FromSeconds(75));
        }
    }

    /// <summary>The client's answer, or null when it didn't answer in time.</summary>
    public async Task<AccountAnswer?> Send(string client, AccountOrder order, CancellationToken stop)
    {
        order.Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        var job = new Job(client, order, new TaskCompletionSource<AccountAnswer>(TaskCreationOptions.RunContinuationsAsynchronously));
        Lane lane;
        lock (gate)
        {
            lane = LaneOf(client);
            lane.Jobs.Add(job);
            lane.Bell?.TrySetResult();
        }
        try
        {
            return await job.Done.Task.WaitAsync(Answer, stop);
        }
        catch (Exception e) when (e is TimeoutException or OperationCanceledException)
        {
            return null;
        }
        finally
        {
            lock (gate)
            {
                lane.Jobs.Remove(job);
                taken.Remove(order.Id);
            }
        }
    }

    /// <summary>The next order for the client, or null when there's none within Hold, or the client has gone.</summary>
    public async Task<AccountOrder?> Next(string client, CancellationToken stop)
    {
        var until = DateTime.UtcNow + Hold;
        Lane lane;
        lock (gate)
        {
            lane = LaneOf(client);
        }
        while (true)
        {
            Task bell;
            lock (gate)
            {
                lane.Asked = DateTime.UtcNow;
                // An order is only handed to a request that is still there to carry it.
                if (lane.Gone || stop.IsCancellationRequested)
                {
                    return null;
                }
                var job = lane.Jobs.FirstOrDefault();
                if (job is not null)
                {
                    lane.Jobs.Remove(job);
                    taken[job.Order.Id!] = job;
                    return job.Order;
                }
                lane.Bell = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                bell = lane.Bell.Task;
                lane.Open++;
            }
            try
            {
                var left = until - DateTime.UtcNow;
                if (left <= TimeSpan.Zero)
                {
                    return null;
                }
                await bell.WaitAsync(left, stop);
            }
            catch (Exception e) when (e is TimeoutException or OperationCanceledException)
            {
                return null;
            }
            finally
            {
                lock (gate)
                {
                    lane.Open--;
                    lane.Asked = DateTime.UtcNow;
                }
            }
        }
    }

    /// <summary>A client's answer to an order it was given. False when it isn't one of its orders, or nobody waits any more.</summary>
    public bool Done(string client, AccountAnswer answer)
    {
        lock (gate)
        {
            if (answer.Id is null || !taken.TryGetValue(answer.Id, out var job) || job.Client != client)
            {
                return false;
            }
            taken.Remove(answer.Id);
            return job.Done.TrySetResult(answer);
        }
    }

    /// <summary>A client was removed: nothing is kept for it, and its open request ends.</summary>
    public void Forget(string client)
    {
        lock (gate)
        {
            if (lanes.Remove(client, out var lane))
            {
                lane.Gone = true;
                lane.Bell?.TrySetResult();
            }
        }
    }

    private Lane LaneOf(string client)
    {
        if (!lanes.TryGetValue(client, out var lane))
        {
            lanes[client] = lane = new Lane();
        }
        return lane;
    }
}
