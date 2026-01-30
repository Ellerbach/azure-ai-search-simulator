using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Core.Models;
using AzureAISearchSimulator.Core.Services;
using Microsoft.Extensions.Options;
using System.Xml;

namespace AzureAISearchSimulator.Api.Services;

/// <summary>
/// Background service that handles scheduled indexer execution.
/// Monitors indexers with schedules and runs them when their scheduled time arrives.
/// </summary>
public class IndexerSchedulerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<IndexerSchedulerService> _logger;
    private readonly IndexerSettings _settings;
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(10);

    // Track next run times for each indexer
    private readonly Dictionary<string, DateTimeOffset> _nextRunTimes = new();

    public IndexerSchedulerService(
        IServiceProvider serviceProvider,
        IOptions<IndexerSettings> settings,
        ILogger<IndexerSchedulerService> logger)
    {
        _serviceProvider = serviceProvider;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.EnableScheduler)
        {
            _logger.LogInformation("Indexer scheduler is disabled");
            return;
        }

        _logger.LogInformation("Indexer scheduler started with check interval: {Interval}", _checkInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndRunScheduledIndexersAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in indexer scheduler");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }

        _logger.LogInformation("Indexer scheduler stopped");
    }

    private async Task CheckAndRunScheduledIndexersAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var indexerService = scope.ServiceProvider.GetRequiredService<IIndexerService>();

        var indexers = await indexerService.ListAsync();
        var now = DateTimeOffset.UtcNow;

        foreach (var indexer in indexers)
        {
            if (stoppingToken.IsCancellationRequested)
                break;

            if (indexer.Disabled == true)
                continue;

            if (indexer.Schedule == null)
                continue;

            try
            {
                await ProcessScheduledIndexerAsync(indexer, indexerService, now);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing scheduled indexer: {Name}", indexer.Name);
            }
        }
    }

    private async Task ProcessScheduledIndexerAsync(
        Indexer indexer,
        IIndexerService indexerService,
        DateTimeOffset now)
    {
        var schedule = indexer.Schedule!;
        var indexerName = indexer.Name;

        // Get the current status to check if it's already running
        var status = await indexerService.GetStatusAsync(indexerName);
        if (status.LastResult?.Status == IndexerExecutionStatus.InProgress)
        {
            _logger.LogDebug("Indexer {Name} is already running, skipping", indexerName);
            return;
        }

        // Calculate next run time if not cached
        if (!_nextRunTimes.TryGetValue(indexerName, out var nextRunTime))
        {
            nextRunTime = CalculateNextRunTime(schedule, status, now);
            _nextRunTimes[indexerName] = nextRunTime;
            _logger.LogInformation("Indexer {Name} scheduled for next run at: {NextRun}", 
                indexerName, nextRunTime);
        }

        // Check if it's time to run
        if (now >= nextRunTime)
        {
            _logger.LogInformation("Running scheduled indexer: {Name}", indexerName);

            try
            {
                // Run the indexer (fire and forget - RunAsync handles its own execution)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await indexerService.RunAsync(indexerName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Scheduled execution failed for indexer: {Name}", indexerName);
                    }
                });

                // Calculate next run time based on interval
                var interval = ParseInterval(schedule.Interval);
                _nextRunTimes[indexerName] = now.Add(interval);
                _logger.LogInformation("Next run for indexer {Name} scheduled at: {NextRun}", 
                    indexerName, _nextRunTimes[indexerName]);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start scheduled indexer: {Name}", indexerName);
            }
        }
    }

    private DateTimeOffset CalculateNextRunTime(
        IndexerSchedule schedule,
        IndexerStatus status,
        DateTimeOffset now)
    {
        var interval = ParseInterval(schedule.Interval);
        var startTime = schedule.StartTime ?? now;

        // If startTime is in the past, run immediately
        if (startTime <= now)
        {
            // Check if this indexer has run before
            if (status.LastResult?.EndTime != null)
            {
                // Calculate next run based on last execution + interval
                var lastRun = status.LastResult.EndTime.Value;
                var nextAfterLast = lastRun.Add(interval);

                // If next scheduled time after last run is still in the past, run immediately
                if (nextAfterLast <= now)
                {
                    return now;
                }

                return nextAfterLast;
            }

            // Never run before, and startTime is in the past - run immediately
            return now;
        }

        // startTime is in the future, wait until then
        return startTime;
    }

    private TimeSpan ParseInterval(string interval)
    {
        try
        {
            // ISO 8601 duration format (e.g., PT5M, PT1H, P1D)
            return XmlConvert.ToTimeSpan(interval);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse interval '{Interval}', defaulting to 5 minutes", interval);
            return TimeSpan.FromMinutes(5);
        }
    }

    /// <summary>
    /// Forces recalculation of next run time for an indexer.
    /// Call this when an indexer is created or updated.
    /// </summary>
    public void InvalidateSchedule(string indexerName)
    {
        _nextRunTimes.Remove(indexerName);
        _logger.LogDebug("Invalidated schedule cache for indexer: {Name}", indexerName);
    }
}
