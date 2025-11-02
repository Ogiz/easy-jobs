namespace BackgroundJobs
{
    public class JobResult
    {
        public JobResult(string name, Guid id, JobStatus status, DateTime? startTime, DateTime? endTime, string? errorMessage, IEnumerable<JobResult>? childJobs = null)
        {
            Name = name;
            Id = id;
            Status = status;
            StartTime = startTime;
            EndTime = endTime;
            ErrorMessage = errorMessage;
            ChildJobs = childJobs?.ToList() ?? new List<JobResult>();
        }

        public string Name { get; }
        public Guid Id { get; }
        public bool IsRunning => Status != JobStatus.Completed && Status != JobStatus.Failed && Status != JobStatus.Cancelled;
        public JobStatus Status { get; }
        public DateTime? StartTime { get; }
        public DateTime? EndTime { get; }
        public string? ErrorMessage { get; }
        public List<JobResult> ChildJobs { get; }
        
        public string? ElapsedTime 
        {
            get
            {
                if (!StartTime.HasValue) return null;
                
                var duration = EndTime.HasValue 
                    ? EndTime.Value - StartTime.Value 
                    : DateTime.UtcNow - StartTime.Value;
                
                return $"{(int)duration.TotalHours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}";
            }
        }
    }
}
