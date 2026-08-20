namespace back_mylife.Services
{
    public class ClassReminderBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ClassReminderBackgroundService> _logger;
        private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(5);

        public ClassReminderBackgroundService(
            IServiceProvider serviceProvider,
            ILogger<ClassReminderBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Class Reminder Background Service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var reminderService = scope.ServiceProvider.GetRequiredService<ClassReminderService>();
                        await reminderService.CheckAndSendReminders();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in Class Reminder Background Service");
                }

                await Task.Delay(_checkInterval, stoppingToken);
            }

            _logger.LogInformation("Class Reminder Background Service stopped");
        }
    }
}
