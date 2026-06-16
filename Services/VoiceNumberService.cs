#if IOS
using Speech;
using AVFoundation;

namespace DailyFantasyMAUI.Services;

/// <summary>
/// iOS continuous speech-to-number service using SFSpeechRecognizer.
/// </summary>
public static class VoiceNumberService
{
    static SFSpeechRecognizer?                  _recognizer;
    static AVAudioEngine?                       _audioEngine;
    static SFSpeechAudioBufferRecognitionRequest? _request;
    static SFSpeechRecognitionTask?             _task;
    static Action<List<int>>?                   _callback;
    static readonly List<int>                   _sentNums = new();

    public static event Action<string>? StatusUpdate;

    public static bool IsAvailable =>
        SFSpeechRecognizer.AuthorizationStatus == SFSpeechRecognizerAuthorizationStatus.Authorized;

    // ── Public API ──────────────────────────────────────────────────────────

    public static void StartContinuous(Action<List<int>> callback)
    {
        _callback = callback;
        _sentNums.Clear();

        // Request both permissions before starting.
        SFSpeechRecognizer.RequestAuthorization(speechStatus =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (speechStatus != SFSpeechRecognizerAuthorizationStatus.Authorized)
                {
                    StatusUpdate?.Invoke("Speech permission denied — enable in Settings");
                    return;
                }
                AVAudioSession.SharedInstance().RequestRecordPermission(micGranted =>
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (!micGranted)
                        {
                            StatusUpdate?.Invoke("Microphone permission denied — enable in Settings");
                            return;
                        }
                        BeginListening();
                    });
                });
            });
        });
    }

    public static void Stop()
    {
        try
        {
            _audioEngine?.Stop();
            _audioEngine?.InputNode.RemoveTapOnBus(0);
        }
        catch { }

        _request?.EndAudio();
        _task?.Cancel();

        _audioEngine  = null;
        _request      = null;
        _task         = null;
        _callback     = null;
        _sentNums.Clear();

        try
        {
            AVAudioSession.SharedInstance().SetActive(
                false, AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation, out _);
        }
        catch { }
    }

    // ── Internal ─────────────────────────────────────────────────────────────

    static void BeginListening()
    {
        try
        {
            _recognizer = new SFSpeechRecognizer(new Foundation.NSLocale("en-US"));
            if (_recognizer == null || !_recognizer.Available)
            {
                StatusUpdate?.Invoke("Speech recognizer not available");
                return;
            }

            _audioEngine = new AVAudioEngine();
            _request     = new SFSpeechAudioBufferRecognitionRequest
            {
                ShouldReportPartialResults = true,
                TaskHint = SFSpeechRecognitionTaskHint.Dictation,
            };

            var session = AVAudioSession.SharedInstance();
            session.SetCategory(AVAudioSessionCategory.PlayAndRecord,
                AVAudioSessionCategoryOptions.DefaultToSpeaker, out _);
            session.SetActive(true, out _);

            var inputNode = _audioEngine.InputNode;
            var format    = inputNode.GetBusOutputFormat(0);
            inputNode.InstallTapOnBus(0, 1024, format, (buffer, _) => _request?.Append(buffer));

            _audioEngine.Prepare();
            NSError? startErr;
            _audioEngine.StartAndReturnError(out startErr);
            if (startErr != null)
            {
                StatusUpdate?.Invoke($"Audio error: {startErr.LocalizedDescription}");
                return;
            }

            _task = _recognizer.GetRecognitionTask(_request, (result, error) =>
            {
                if (result == null) return;

                var text    = result.BestTranscription.FormattedString ?? "";
                var allNums = ParseNumbers(text);

                // Only dispatch numbers we haven't sent yet.
                var newNums = allNums.Count > _sentNums.Count
                    ? allNums.GetRange(_sentNums.Count, allNums.Count - _sentNums.Count)
                    : new List<int>();

                if (newNums.Count > 0)
                {
                    _sentNums.AddRange(newNums);
                    var captured = newNums;
                    MainThread.BeginInvokeOnMainThread(() => _callback?.Invoke(captured));
                }

                // On final result, restart so the user can keep speaking.
                if (result.Final)
                {
                    _sentNums.Clear();
                    RestartListening();
                }
            });

            StatusUpdate?.Invoke("🔴 Listening...");
        }
        catch (Exception ex)
        {
            StatusUpdate?.Invoke($"Voice error: {ex.Message}");
        }
    }

    static void RestartListening()
    {
        // Tear down current session then start fresh.
        try { _audioEngine?.Stop(); _audioEngine?.InputNode.RemoveTapOnBus(0); } catch { }
        _request?.EndAudio();
        _task?.Cancel();
        _audioEngine = null;
        _request     = null;
        _task        = null;

        if (_callback != null)
            MainThread.BeginInvokeOnMainThread(BeginListening);
    }

    static List<int> ParseNumbers(string text)
    {
        var result = new List<int>();
        var words  = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var w in words)
        {
            // Numeric digit string ("5", "19")
            if (int.TryParse(w, out int n) && n >= 1 && n <= 99)
            {
                result.Add(n);
                continue;
            }
            // Word-form ("five", "nineteen", "thirty-three")
            int? wn = WordToNumber(w.ToLowerInvariant());
            if (wn.HasValue && wn.Value >= 1 && wn.Value <= 99)
                result.Add(wn.Value);
        }
        return result;
    }

    static int? WordToNumber(string w) => w switch
    {
        "one"          => 1,  "two"         => 2,  "three"       => 3,
        "four"         => 4,  "five"        => 5,  "six"         => 6,
        "seven"        => 7,  "eight"       => 8,  "nine"        => 9,
        "ten"          => 10, "eleven"      => 11, "twelve"      => 12,
        "thirteen"     => 13, "fourteen"    => 14, "fifteen"     => 15,
        "sixteen"      => 16, "seventeen"   => 17, "eighteen"    => 18,
        "nineteen"     => 19, "twenty"      => 20, "thirty"      => 30,
        "forty"        => 40, "fifty"       => 50, "sixty"       => 60,
        "seventy"      => 70, "eighty"      => 80, "ninety"      => 90,
        _              => null
    };
}
#endif
