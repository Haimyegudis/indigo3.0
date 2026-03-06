using IndiLogs_3._0.Services.Interfaces;

namespace IndiLogs_3._0.Services
{
    /// <summary>
    /// Groups all grep-related service dependencies into a single injectable bundle.
    /// Avoids excessive constructor parameters when passing grep services through ViewModels.
    /// </summary>
    public sealed class GrepServiceBundle
    {
        public IGlobalGrepService GrepService { get; }
        public ISearchLocationService LocationService { get; }
        public ISearchConfigService ConfigService { get; }
        public ISearchSchedulerService SchedulerService { get; }
        public IWindowsTaskSchedulerService TaskSchedulerService { get; }
        public IEmailNotificationService EmailService { get; }

        public GrepServiceBundle(
            IGlobalGrepService grepService,
            ISearchLocationService locationService,
            ISearchConfigService configService,
            ISearchSchedulerService schedulerService,
            IWindowsTaskSchedulerService taskSchedulerService,
            IEmailNotificationService emailService)
        {
            GrepService = grepService;
            LocationService = locationService;
            ConfigService = configService;
            SchedulerService = schedulerService;
            TaskSchedulerService = taskSchedulerService;
            EmailService = emailService;
        }
    }
}
