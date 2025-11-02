using System.Collections.Concurrent;

namespace BackgroundJobs.Tests.TestJobs
{
    public class ParallelOrchestratorJob : Job
    {
        private readonly List<IJob> _childJobs;
        private readonly ConcurrentDictionary<Guid, JobResult> _childResultsDict;
        private readonly ConcurrentDictionary<Guid, Action<JobResult>> _statusHandlers = new();
        private readonly bool _continueOnFailure;

        public override string Name { get; }

        public ParallelOrchestratorJob(List<IJob> childJobs, bool continueOnFailure = false)
        {
            _childJobs = childJobs ?? throw new ArgumentNullException(nameof(childJobs));
            _continueOnFailure = continueOnFailure;
            Name = $"ParallelOrchestrator-{Guid.NewGuid()}";
            _childResultsDict = new ConcurrentDictionary<Guid, JobResult>(
                _childJobs.ToDictionary(job => job.Id, job => job.GetResult())
            );

            foreach (IJob job in _childJobs)
            {
                Action<JobResult> handler = result => _childResultsDict[result.Id] = result;
                _statusHandlers[job.Id] = handler;
                job.StatusChanged += handler;
            }
        }

        protected override async Task ExecuteInternal(CancellationToken ct)
        {
            foreach (IJob childJob in _childJobs)
            {
                childJob.Execute();
            }

            if (_continueOnFailure)
            {
                List<Task> waitTasks = _childJobs.Select(async job =>
                {
                    JobResult result = await job.WaitForCompletionAsync(
                        onFailure: failedResult => Task.FromResult(failedResult),
                        onCancellation: cancelledResult => Task.FromResult(cancelledResult),
                        ct
                    );
                    _childResultsDict[job.Id] = result;
                }).ToList();

                await Task.WhenAll(waitTasks);
            }
            else
            {
                List<Task> waitTasks = _childJobs.Select(async job =>
                {
                    JobResult result = await job.WaitForCompletionAsync(ct);
                    _childResultsDict[job.Id] = result;
                }).ToList();

                await Task.WhenAll(waitTasks);
            }
        }

        protected override JobResult GetResultInternal()
        {
            JobResult baseResult = base.GetResultInternal();

            List<JobResult> childResults = _childJobs
                .Select(job => _childResultsDict[job.Id])
                .ToList();

            return new JobResult(
                baseResult.Name,
                baseResult.Id,
                baseResult.Status,
                baseResult.StartTime,
                baseResult.EndTime,
                baseResult.ErrorMessage,
                childResults
            );
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (IJob childJob in _childJobs)
                {
                    if (_statusHandlers.TryGetValue(childJob.Id, out var handler))
                    {
                        childJob.StatusChanged -= handler;
                    }
                    childJob?.Dispose();
                }
            }

            base.Dispose(disposing);
        }
    }
}
