using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using DailyFantasyMAUI.Services;

namespace DailyFantasyMAUI.Services;

public static class SmtpSmsService
{
    public const string PrefGmail   = "sms_gmail";
    public const string PrefGmailPw = "sms_gmail_pw";
    const string PrefLastSms        = "sms_last_date";

    static readonly Dictionary<string, string> Gateways = new()
    {
        ["att"]       = "mms.att.net",
        ["tmobile"]   = "tmomail.net",
        ["verizon"]   = "vtext.com",
        ["xfinity"]   = "vtext.com",   // Xfinity Mobile runs on Verizon's network
        ["sprint"]    = "messaging.sprintpcs.com",
        ["boost"]     = "sms.myboostmobile.com",
        ["cricket"]   = "sms.cricketwireless.net",
        ["metro"]     = "mymetropcs.com",
        ["uscellular"]= "email.uscc.net",
    };

    public static async Task<(bool Ok, string Error)> SendAsync(
        string gmail, string appPassword, string toPhone, string carrier, string body)
    {
        try
        {
            if (!Gateways.TryGetValue(carrier, out string? gateway))
                return (false, $"Unknown carrier '{carrier}'");

            string digits = new string(toPhone.Where(char.IsDigit).ToArray());
            if (digits.Length < 10)
                return (false, "Phone number must be at least 10 digits");

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("Lottery", gmail));
            message.To.Add(new MailboxAddress("", $"{digits}@{gateway}"));
            message.Subject = "";
            message.Body = new TextPart("plain") { Text = body };

            using var client = new SmtpClient();
            await client.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(gmail, appPassword);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            return (true, "");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// Called on app launch. Sends the daily SMS once per day if SMS is enabled and
    /// credentials/phone are configured. Safe to call multiple times — skips if already sent today.
    public static async Task<bool> TrySendDailyIfNeededAsync()
    {
        if (!Preferences.Get("notif_sms_enabled", false)) return false;

        string today = DateTime.Today.ToString("yyyyMMdd");
        if (Preferences.Get(PrefLastSms, "") == today) return false;

        string phone   = Preferences.Get("notif_phone", "");
        string carrier = Preferences.Get("notif_carrier", "att");
        string gmail   = Preferences.Get(PrefGmail, "");
        string pw      = Preferences.Get(PrefGmailPw, "");

        if (phone.Length < 10 || string.IsNullOrEmpty(gmail) || string.IsNullOrEmpty(pw))
            return false;

        string summary = AdvancePlayNotificationService.BuildSummary();
        if (summary == "No active advance play dates.") return false;

        var (ok, _) = await SendAsync(gmail, pw, phone, carrier, $"Lottery Daily Update:\n{summary}");
        if (ok) Preferences.Set(PrefLastSms, today);
        return ok;
    }
}
