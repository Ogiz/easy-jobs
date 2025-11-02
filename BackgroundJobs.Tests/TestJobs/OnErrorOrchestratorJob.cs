namespace BackgroundJobs.Tests.TestJobs
{
    public class OnErrorOrchestratorJob : Job
    {
        private readonly List<IJob> _childJobs;
        private readonly List<JobResult> _childResults;
        private readonly List<Action<JobResult>> _statusHandlers = new();
        private readonly List<Exception> _errors = new();

        public override string Name { get; }
        public IReadOnlyList<Exception> Errors => _errors.AsReadOnly();

        public OnErrorOrchestratorJob(List<IJob> childJobs)
        {
            _childJobs = childJobs ?? throw new ArgumentNullException(nameof(childJobs));
            Name = $"OnErrorOrchestrator-{Guid.NewGuid()}";
            _childResults = _childJobs.Select(job => job.GetResult()).ToList();

            for (int i = 0; i < _childJobs.Count; i++)
            {
                int index = i;
                Action<JobResult> handler = result => _childResults[index] = result;
                _statusHandlers.Add(handler);
                _childJobs[i].StatusChanged += handler;
            }
        }

        protected override async Task ExecuteInternal(CancellationToken ct)
        {
            for (int i = 0; i < _childJobs.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                IJob childJob = _childJobs[i];
                childJob.Execute();

                try
                {
                    _childResults[i] = await childJob.WaitForCompletionAsync(ct);
                }
                catch (Exception ex)
                {
                    _childResults[i] = childJob.GetResult();
                    throw new InvalidOperationException($"Child job '{childJob.Name}' failed", ex);
                }
            }
        }

        protected override void OnError(Exception ex)
        {
            _errors.Add(ex);
        }

        protected override JobResult GetResultInternal()
        {
            JobResult baseResult = base.GetResultInternal();

            return new JobResult(
                baseResult.Name,
                baseResult.Id,
                baseResult.Status,
                baseResult.StartTime,
                baseResult.EndTime,
                baseResult.ErrorMessage,
                _childResults
            );
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                for (int i = 0; i < _childJobs.Count; i++)
                {
                    _childJobs[i].StatusChanged -= _statusHandlers[i];
                    _childJobs[i]?.Dispose();
                }
            }

            base.Dispose(disposing);
        }
    }
}
