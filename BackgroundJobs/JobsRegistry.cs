using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace BackgroundJobs
{
    public class JobsRegistry
    {
        private static readonly Lazy<JobsRegistry> _instance = new(() => new JobsRegistry());

        [SuppressMessage("ReSharper", "NotAccessedField.Local", Justification = "Timer is used for housekeeping tasks")]
        private readonly Timer _housekeepingTimer;

        private readonly object _configLock = new();
        private TimeSpan _jobRetentionTime = TimeSpan.FromMinutes(10);
        private int _maxJobs = 1000;

        private readonly ConcurrentDictionary<Guid, IJob> _jobsById = new();
        private readonly ConcurrentDictionary<string, IJob> _jobsByName = new();

        public static JobsRegistry Instance => _instance.Value;

        public TimeSpan JobRetentionTime
        {
            get => _jobRetentionTime;
            set
            {
                lock (_configLock)
                {
                    _jobRetentionTime = value;
                }
            }
        }

        public int MaxJobs
        {
            get => _maxJobs;
            set
            {
                lock (_configLock)
                {
                    _maxJobs = value;
                }
            }
        }

        private JobsRegistry()
        {
            _housekeepingTimer = new Timer(HousekeepingCallback, null,
                TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        public JobResult RegisterJob(IJob job)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));

            JobResult currentResult = job.GetResult();
            if (!currentResult.IsRunning)
            {
                job.Dispose();
                return currentResult;
            }

            IJob existingJobByName = _jobsByName.GetOrAdd(job.Name, job);

            if (existingJobByName.Id != job.Id)
            {
                job.Dispose(); 
                return existingJobByName.GetResult();
            }

            IJob existingJobById = _jobsById.GetOrAdd(job.Id, job);

            if (existingJobById.Name != job.Name)
            {
                _jobsByName.TryRemove(job.Name, out _);
                job.Cancel("Job registration failed due to duplicate ID.");
                job.Dispose();
                return job.GetResult();
            }

            job.Execute();

            return job.GetResult();
        }

        public JobResult GetJobById(Guid id)
        {
            if (_jobsById.TryGetValue(id, out IJob? job))
            {
                return job.GetResult();
            }

            return new JobResult("Unknown", id, JobStatus.Completed, DateTime.UtcNow, DateTime.UtcNow, "no job in registry");
        }

        public IJob GetJobByName(string name)
        {
            if (_jobsByName.TryGetValue(name, out IJob? job))
            {
                return job;
            }

            throw new KeyNotFoundException($"Job with name '{name}' not found.");
        }

        public IEnumerable<JobResult> GetAllJobs()
        {
            return _jobsById.Values.Select(job => job.GetResult()).ToList();
        }

        public JobResult CancelJob(Guid id)
        {
            if (_jobsById.TryGetValue(id, out IJob? job))
            {
                job.Cancel();
                return job.GetResult();
            }

            return new JobResult("Unknown", id, JobStatus.Completed, DateTime.UtcNow, DateTime.UtcNow, "no job in registry");
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
            DateTime cutoff = DateTime.UtcNow - _jobRetentionTime;
            List<Guid> jobsToCleanup = _jobsById.Values
                .Where(job => ShouldCleanup(job, cutoff))
                .Select(job => job.Id)
                .ToList();

            foreach (Guid jobId in jobsToCleanup)
            {
                TryCleanupJob(jobId);
            }
        }

        private bool ShouldCleanup(IJob job, DateTime cutoff)
        {
            JobResult? result = job.GetResult();
            return result?.EndTime.HasValue == true && result.EndTime.Value < cutoff;
        }

        private bool TryCleanupJob(Guid jobId)
        {
            if (_jobsById.TryRemove(jobId, out IJob? job))
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

            List<Guid> finishedJobs = _jobsById.Values
                .Where(IsJobFinished)
                .OrderBy(job => job.GetResult()?.EndTime)
                .Take(_jobsById.Count - _maxJobs)
                .Select(job => job.Id)
                .ToList();

            foreach (Guid jobId in finishedJobs)
            {
                TryCleanupJob(jobId);
            }
        }

        private bool IsJobFinished(IJob job)
        {
            try
            {
                JobResult? result = job.GetResult();
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
