namespace BackgroundJobs.Tests.TestJobs
{
    public class TrackableJob : Job
    {
        public bool ExecuteInternalCalled { get; private set; }

        public override string Name { get; }

        public TrackableJob()
        {
            Name = $"TrackableJob-{Guid.NewGuid()}";
        }

        protected override Task ExecuteInternal(CancellationToken ct)
        {
            ExecuteInternalCalled = true;
            return Task.CompletedTask;
        }
    }
}
