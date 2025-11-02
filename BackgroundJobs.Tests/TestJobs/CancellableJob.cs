namespace BackgroundJobs.Tests.TestJobs
{
    public class CancellableJob : Job
    {
        private readonly TimeSpan _duration;

        public override string Name { get; }

        public CancellableJob(TimeSpan? duration = null)
        {
            _duration = duration ?? TimeSpan.FromSeconds(5);
            Name = $"CancellableJob-{Guid.NewGuid()}";
        }

        protected override async Task ExecuteInternal(CancellationToken ct)
        {
            await Task.Delay(_duration, ct);
        }
    }
}
