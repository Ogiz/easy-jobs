using BackgroundJobs;

namespace WebApiExample.Jobs;

public class SuccessJob : Job
{
    public override string Name { get; }

    public SuccessJob(string name)
    {
        Name = name;
    }

    protected override Task ExecuteInternal(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
