using System.Collections.Concurrent;
using BackgroundJobs;

namespace WebApiExample.Jobs;

public class ParallelOrchestratorJob : Job
{
    private readonly List<BackgroundJobs.IJob> _jobs;
    private readonly bool _continueOnFailure;
    private readonly ConcurrentDictionary<Guid, JobResult> _childResults = new();
    public override string Name { get; }

    public ParallelOrchestratorJob(string name, List<BackgroundJobs.IJob> jobs, bool continueOnFailure = false)
    {
        Name = name;
        _jobs = jobs;
        _continueOnFailure = continueOnFailure;
    }

    protected override async Task ExecuteInternal(CancellationToken cancellationToken)
    {
        try
        {
            foreach (var job in _jobs)
            {
                job.StatusChanged += (result) =>
                {
                    _childResults[result.Id] = result;
                };
            }

            foreach (var job in _jobs)
            {
                job.Execute();
            }

            var tasks = _jobs.Select(job => job.WaitForCompletionAsync(cancellationToken: cancellationToken)).ToList();

            if (_continueOnFailure)
            {
                await Task.WhenAll(tasks.Select(t => t.ContinueWith(_ => { }, cancellationToken)));
            }
            else
            {
                await Task.WhenAll(tasks);
            }
        }
        finally
        {
            foreach (var job in _jobs)
            {
                _childResults[job.Id] = job.GetResult();
                job.Dispose();
            }
        }
    }

    protected override JobResult GetResultInternal()
    {
        var result = base.GetResultInternal();
        var childJobs = _jobs.Select(job => _childResults.GetValueOrDefault(job.Id)).Where(r => r != null).ToList();
        return new JobResult(result.Name, result.Id, result.Status, result.StartTime, result.EndTime, result.ErrorMessage, childJobs!);
    }
}
