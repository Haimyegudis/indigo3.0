namespace IndiLogs_3._0.Services.Interfaces
{
    public interface IGrepServiceBundle
    {
        IGlobalGrepService GrepService { get; }
        ISearchLocationService LocationService { get; }
        ISearchConfigService ConfigService { get; }
        ISearchSchedulerService SchedulerService { get; }
        IWindowsTaskSchedulerService TaskSchedulerService { get; }
        IEmailNotificationService EmailService { get; }
    }
}
