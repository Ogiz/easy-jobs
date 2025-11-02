using BackgroundJobs;

namespace WebApiExample.Jobs;

public class FailingJob : Job
{
    private readonly string _errorMessage;
    public override string Name { get; }

    public FailingJob(string name, string errorMessage = "Job failed")
    {
        Name = name;
        _errorMessage = errorMessage;
    }

    protected override Task ExecuteInternal(CancellationToken cancellationToken)
    {
        throw new InvalidOperationException(_errorMessage);
    }
}
