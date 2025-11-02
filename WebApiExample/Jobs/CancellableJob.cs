using BackgroundJobs;

namespace WebApiExample.Jobs;

public class CancellableJob : Job
{
    private readonly TimeSpan _duration;
    public override string Name { get; }

    public CancellableJob(string name, TimeSpan duration)
    {
        Name = name;
        _duration = duration;
    }

    protected override async Task ExecuteInternal(CancellationToken cancellationToken)
    {
        await Task.Delay(_duration, cancellationToken);
    }
}
