using BackgroundTasks;
using Foundation;
using DailyFantasyMAUI.Services;

namespace DailyFantasyMAUI;

// iOS counterpart to the Android app's Platforms/Android/HotSpotFastCheckScheduler.cs +
// HotSpotFastCheckReceiver.cs. Android arms a self-rescheduling AlarmManager chain that fires
// every ~1 minute while a saved-but-unreviewed Hot Spot ticket exists — iOS has NO equivalent:
// Apple's background execution model does not let third-party apps run code on a guaranteed
// short interval. BGTaskScheduler's BGAppRefreshTask is the closest available mechanism, but
// iOS — not this app — decides when (or whether) it actually runs; in practice that can be
// anywhere from minutes to several hours, influenced by the user's real usage patterns, battery
// state, etc. This is a genuine, permanent difference from Android, not a bug to chase.
//
// HasPendingTicket()'s own logic is copied verbatim from Android's version — it's pure
// Preferences/HotSpotPage lookups, no platform API involved.
public static class HotSpotFastCheckScheduler
{
    const string TaskId = "com.calho.dailyfantasyios.hotspotcheck";

    public static bool HasPendingTicket()
    {
        for (int slot = 0; slot < HotSpotPage.SlotCount; slot++)
        {
            string numbers = Preferences.Get(HotSpotPage.SlotKey(HotSpotPage.KeyNumbers, slot), "");
            if (string.IsNullOrWhiteSpace(numbers)) continue;
            int startDraw = Preferences.Get(HotSpotPage.SlotKey(HotSpotPage.KeyStartDraw, slot), 0);
            if (startDraw <= 0) continue;
            if (Preferences.Get(HotSpotPage.SlotKey(HotSpotPage.KeyReviewed, slot), false)) continue;
            return true;
        }
        return false;
    }

    /// Call whenever a Hot Spot ticket is saved/edited, and once at app startup.
    public static void EnsureScheduled()
    {
        if (HasPendingTicket()) ScheduleNext();
        else Cancel();
    }

    /// Submits a best-effort BGAppRefreshTask request — iOS may run it anywhere from a few
    /// minutes to several hours from now, or skip it entirely under some conditions. One-shot
    /// per submission (unlike an Android alarm), so the handler in RegisterBackgroundTask below
    /// re-submits a fresh request each time it runs, forming its own self-rescheduling chain.
    public static void ScheduleNext()
    {
        try
        {
            var request = new BGAppRefreshTaskRequest(TaskId)
            {
                EarliestBeginDate = (NSDate)DateTime.Now.AddMinutes(1),
            };
            BGTaskScheduler.Shared.Submit(request, out _);
        }
        catch { }
    }

    public static void Cancel()
    {
        try { BGTaskScheduler.Shared.Cancel(TaskId); } catch { }
    }

    // Must be called from AppDelegate.FinishedLaunching, before it returns, per Apple's
    // BGTaskScheduler requirement — see AppDelegate.cs.
    public static void RegisterBackgroundTask()
    {
        BGTaskScheduler.Shared.Register(TaskId, null, async task =>
        {
            var refreshTask = (BGAppRefreshTask)task;
            refreshTask.ExpirationHandler = () => refreshTask.SetTaskCompleted(false);
            try
            {
                var newlyRecorded = await HotSpotChecker.CheckFinishedTicketsAsync();
                if (newlyRecorded.Count > 0)
                {
                    decimal total = newlyRecorded.Sum(w => w.Amount);
                    string details = string.Join("\n", newlyRecorded.Select(w => $"${w.Amount:N2} — {w.Note}"));
                    NotificationHelper.ShowWin($"You Won ${total:N0} on Hot Spot!", details);
                }
                refreshTask.SetTaskCompleted(true);
            }
            catch
            {
                refreshTask.SetTaskCompleted(false);
            }
            finally
            {
                // Keep the chain alive only while there's still something to check — otherwise
                // this would keep waking the app in the background indefinitely for no reason.
                if (HasPendingTicket()) ScheduleNext();
            }
        });
    }
}
