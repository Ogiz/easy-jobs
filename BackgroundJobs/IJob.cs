namespace BackgroundJobs
{
    public interface IJob : IDisposable
    {
        string Name { get; }
        Guid Id { get; }
        void Execute();
        JobResult GetResult();
        void Cancel(string reason = "User requested cancellation");
        event Action<JobResult> StatusChanged;
    }
}
