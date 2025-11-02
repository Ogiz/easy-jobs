using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace BackgroundJobs
{
    public class JobsRegistry
    {
        private static readonly Lazy<JobsRegistry> _instance = new(() => new JobsRegistry());

        [SuppressMessage("ReSharper", "NotAccessedField.Local", Justification = "Timer is used for housekeeping tasks")]
        private readonly Timer _housekeepingTimer;

        private readonly TimeSpan _jobRetentionTime = TimeSpan.FromMinutes(10);
        private readonly ConcurrentDictionary<Guid, IJob> _jobsById = new();
        private readonly ConcurrentDictionary<string, IJob> _jobsByName = new();
        private readonly int _maxJobs = 1000;

        public static JobsRegistry Instance => _instance.Value;

        private JobsRegistry()
        {
            _housekeepingTimer = new Timer(HousekeepingCallback, null,
                TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        public JobResult RegisterJob(IJob job)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));

            if (!_jobsByName.TryAdd(job.Name, job))
            {
                throw new InvalidOperationException($"Job with name '{job.Name}' already exists.");
            }

            job.StatusChanged += JobOnStatusChanged;
            job.Execute();

            if (!_jobsById.TryAdd(job.Id, job))
            {
                _jobsByName.TryRemove(job.Name, out _);
                job.Cancel("Job registration failed due to duplicate ID.");
                job.Dispose();
            }

            return job.GetResult();
        }

        public IJob GetJobById(Guid id)
        {
            if (_jobsById.TryGetValue(id, out var job))
            {
                return job;
            }

            throw new KeyNotFoundException($"Job with ID '{id}' not found.");
        }

        public IJob GetJobByName(string name)
        {
            if (_jobsByName.TryGetValue(name, out var job))
            {
                return job;
            }

            throw new KeyNotFoundException($"Job with name '{name}' not found.");
        }

        public IEnumerable<JobResult> GetAllJobs()
        {
            return _jobsById.Values.Select(job => job.GetResult()).ToList();
        }

        private void JobOnStatusChanged(JobResult result)
        {
            // Can be extended for logging or notifications
        }

        private void HousekeepingCallback(object? state)
        {
            try
            {
                CleanupFinishedJobs();
                EnforceJobLimit();
            }
            catch
            {
                // Ignore housekeeping exceptions
            }
        }

        private void CleanupFinishedJobs()
        {
            var cutoff = DateTime.UtcNow - _jobRetentionTime;
            var jobsToCleanup = _jobsById.Values
                .Where(job => ShouldCleanup(job, cutoff))
                .Select(job => job.Id)
                .ToList();

            foreach (var jobId in jobsToCleanup)
            {
                TryCleanupJob(jobId);
            }
        }

        private bool ShouldCleanup(IJob job, DateTime cutoff)
        {
            var result = job.GetResult();
            return result?.EndTime.HasValue == true && result.EndTime.Value < cutoff;
        }

        private bool TryCleanupJob(Guid jobId)
        {
            if (_jobsById.TryRemove(jobId, out var job))
            {
                _jobsByName.TryRemove(job.Name, out _);
                job.Dispose();
                return true;
            }

            return false;
        }

        private void EnforceJobLimit()
        {
            if (_jobsById.Count <= _maxJobs) return;

            var finishedJobs = _jobsById.Values
                .Where(job => IsJobFinished(job))
                .OrderBy(job => job.GetResult()?.EndTime)
                .Take(_jobsById.Count - _maxJobs)
                .Select(job => job.Id)
                .ToList();

            foreach (var jobId in finishedJobs)
            {
                TryCleanupJob(jobId);
            }
        }

        private bool IsJobFinished(IJob job)
        {
            try
            {
                var result = job.GetResult();
                return result?.Status == JobStatus.Completed ||
                       result?.Status == JobStatus.Failed ||
                       result?.Status == JobStatus.Cancelled;
            }
            catch
            {
                return false;
            }
        }
    }
}
