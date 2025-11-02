using BackgroundJobs;

namespace WebApiExample.Jobs;

public class SequentialOrchestratorJob : Job
{
    private readonly List<BackgroundJobs.IJob> _jobs;
    private readonly bool _continueOnFailure;
    private readonly List<JobResult> _childResults = new();
    public override string Name { get; }

    public SequentialOrchestratorJob(string name, List<BackgroundJobs.IJob> jobs, bool continueOnFailure = false)
    {
        Name = name;
        _jobs = jobs;
        _continueOnFailure = continueOnFailure;

        for (int i = 0; i < _jobs.Count; i++)
        {
            _childResults.Add(null!);
        }
    }

    protected override async Task ExecuteInternal(CancellationToken cancellationToken)
    {
        try
        {
            for (int i = 0; i < _jobs.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var job = _jobs[i];
                int index = i;

                job.StatusChanged += (result) =>
                {
                    _childResults[index] = result;
                };

                job.Execute();

                try
                {
                    await job.WaitForCompletionAsync(cancellationToken: cancellationToken);
                }
                catch when (_continueOnFailure)
                {
                }
                finally
                {
                    _childResults[index] = job.GetResult();
                }
            }
        }
        finally
        {
            foreach (var job in _jobs)
            {
                job.Dispose();
            }
        }
    }

    protected override JobResult GetResultInternal()
    {
        var result = base.GetResultInternal();
        var childJobs = _childResults.Where(r => r != null).ToList();
        return new JobResult(result.Name, result.Id, result.Status, result.StartTime, result.EndTime, result.ErrorMessage, childJobs);
    }
}
