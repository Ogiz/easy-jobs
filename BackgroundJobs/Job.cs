namespace BackgroundJobs
{
    public abstract class Job : IJob
    {
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly object _statusLock = new();
        private DateTime? _endTime;
        private string? _errorMessage;
        private Task? _executionTask;
        private DateTime? _startTime;
        private JobStatus _status = JobStatus.Pending;

        public Guid Id { get; }
        public abstract string Name { get; }

        protected Job()
        {
            Id = Guid.NewGuid();
            _cancellationTokenSource = new CancellationTokenSource();
        }

        public event Action<JobResult>? StatusChanged;

        public virtual void Dispose()
        {
            _cancellationTokenSource.Dispose();
        }

        public void Execute()
        {
            lock (_statusLock)
            {
                if (_status != JobStatus.Pending) return;
                _status = JobStatus.Running;
                _startTime = DateTime.UtcNow;
            }
            
            _ = NotifyStatusChangedAsync();
            
            // Force execution onto ThreadPool to avoid ASP.NET context issues
            _executionTask = ExecuteWrapper(_cancellationTokenSource.Token);
        }

        private async Task ExecuteWrapper(CancellationToken ct)
        {
            try
            {
                await ExecuteInternal(ct);
                
                lock (_statusLock)
                {
                    if (_status == JobStatus.Running)
                    {
                        _status = JobStatus.Completed;
                        _endTime = DateTime.UtcNow;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                lock (_statusLock)
                {
                    if (_status == JobStatus.Running)
                    {
                        _status = JobStatus.Cancelled;
                        _endTime = DateTime.UtcNow;
                    }
                }
            }
            catch (Exception ex)
            {
                lock (_statusLock)
                {
                    if (_status == JobStatus.Running)
                    {
                        _status = JobStatus.Failed;
                        _endTime = DateTime.UtcNow;
                        _errorMessage = ex.Message;
                    }
                }
                
                OnError(ex);
            }
            finally
            {
                _ = NotifyStatusChangedAsync();
            }
        }

        protected abstract Task ExecuteInternal(CancellationToken ct);

        protected virtual void OnError(Exception ex)
        {
            // Override for custom error handling
        }

        public JobResult GetResult()
        {
            lock (_statusLock)
            {
                return GetResultInternal();
            }
        }
        
        protected virtual JobResult GetResultInternal()
        {
            return new JobResult(Name, Id, _status, _startTime, _endTime, _errorMessage);
        }

        public void Cancel(string reason = "User requested cancellation")
        {
            bool shouldCancel;

            lock (_statusLock)
            {
                shouldCancel = _status == JobStatus.Pending || _status == JobStatus.Running;
                
                if (shouldCancel)
                {
                    _status = JobStatus.Cancelled;
                    _errorMessage = reason;
                    _endTime = DateTime.UtcNow;
                }
            }

            if (!shouldCancel) return;

            _cancellationTokenSource.Cancel();
            _ = NotifyStatusChangedAsync();

            if (_executionTask == null) return;

            try
            {
                _executionTask.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                // Expected when cancelled
            }
        }

        private async Task NotifyStatusChangedAsync()
        {
            if (StatusChanged == null) return;

            try
            {
                JobResult result;
                lock (_statusLock)
                {
                    result = GetResultInternal();
                }
                
                await Task.Run(() => StatusChanged?.Invoke(result));
            }
            catch
            {
                // Ignore exceptions in status notification
            }
        }
    }
}
