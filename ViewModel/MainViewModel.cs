using DailyFantasyMAUI.Model;
using DailyFantasyMAUI.Parsing;
using DailyFantasyMAUI.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DailyFantasyMAUI.ViewModel
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private ObservableCollection<ModelDaily> _dataFantasy = new();
        private ObservableCollection<ModelDaily> _dataSuperLotto = new();
        private ObservableCollection<ModelDaily> _dataDaily3 = new();
        private ObservableCollection<string> _picks = new();
        private ObservableCollection<string> _combinations = new();
        private ObservableCollection<ModelDaily> _recurrenceResults = new();
        private ObservableCollection<string> _drawsHistory = new();
        private string _lastNumberHit = "Loading...";
        private double _possibleCombinations;
        private int _numberInList;
        private bool _isLoading;
        private int _activeTab; // 0=History, 1=Recurrence, 2=Combos, 3=Draws
        private string _statusMessage = "";

        // Shared across pages so Daily3Page can access the last generated combos
        public static List<string> SharedCombos { get; set; } = new();

        readonly ComboCalc _calc = new();

        public ObservableCollection<ModelDaily> DataFantasy { get => _dataFantasy; set { _dataFantasy = value; OnPropertyChanged(); } }
        public ObservableCollection<ModelDaily> DataSuperLotto { get => _dataSuperLotto; set { _dataSuperLotto = value; OnPropertyChanged(); } }
        public ObservableCollection<ModelDaily> DataDaily3 { get => _dataDaily3; set { _dataDaily3 = value; OnPropertyChanged(); } }
        public ObservableCollection<string> Picks { get => _picks; set { _picks = value; OnPropertyChanged(); } }
        public ObservableCollection<string> Combinations { get => _combinations; set { _combinations = value; OnPropertyChanged(); } }
        public ObservableCollection<ModelDaily> RecurrenceResults { get => _recurrenceResults; set { _recurrenceResults = value; OnPropertyChanged(); } }
        public ObservableCollection<string> DrawsHistory { get => _drawsHistory; set { _drawsHistory = value; OnPropertyChanged(); } }
        public string LastNumberHit { get => _lastNumberHit; set { _lastNumberHit = value; OnPropertyChanged(); } }
        public double PossibleCombinations { get => _possibleCombinations; set { _possibleCombinations = value; OnPropertyChanged(); } }
        public int NumberInList { get => _numberInList; set { _numberInList = value; OnPropertyChanged(); } }
        public bool IsLoading { get => _isLoading; set { _isLoading = value; OnPropertyChanged(); } }
        public int ActiveTab { get => _activeTab; set { _activeTab = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowHistory)); OnPropertyChanged(nameof(ShowRecurrence)); OnPropertyChanged(nameof(ShowCombos)); OnPropertyChanged(nameof(ShowDraws)); } }
        public string StatusMessage { get => _statusMessage; set { _statusMessage = value; OnPropertyChanged(); } }

        public bool ShowHistory => _activeTab == 0;
        public bool ShowRecurrence => _activeTab == 1;
        public bool ShowCombos => _activeTab == 2;
        public bool ShowDraws => _activeTab == 3;

        public async Task LoadDataAsync()
        {
            IsLoading = true;
            StatusMessage = "Loading draw history...";

            // Load from bundled/local files immediately — no network wait
            await LoadFromFileAsync();
            await LoadSLFromFileAsync();
            await LoadD3FromFileAsync();

            if (_dataFantasy.Count > 0)
            {
                var latest = _dataFantasy[0];
                LastNumberHit = $"[{latest.DrawDate}] {latest.N1}, {latest.N2}, {latest.N3}, {latest.N4}, {latest.N5}";
            }
            else
            {
                LastNumberHit = "No draw data available";
            }
            IsLoading = false;
            StatusMessage = $"Ready — {_dataFantasy.Count} F5 · {_dataSuperLotto.Count} SL · {_dataDaily3.Count} D3 draws";

            // Update CSVs from API in background — don't block the UI
            _ = UpdateAllCsvsInBackgroundAsync();
        }

        private async Task UpdateAllCsvsInBackgroundAsync()
        {
            try
            {
                await Task.Run(async () =>
                {
                    await GetDataEntry.UpdateF5CsvAsync();
                    await GetDataEntry.UpdateSLCsvAsync();
                    await GetDataEntry.UpdateD3CsvAsync();
                });

                // Reload collections on the main thread after update
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await LoadFromFileAsync();
                    await LoadSLFromFileAsync();
                    await LoadD3FromFileAsync();
                    if (_dataFantasy.Count > 0)
                    {
                        var latest = _dataFantasy[0];
                        LastNumberHit = $"[{latest.DrawDate}] {latest.N1}, {latest.N2}, {latest.N3}, {latest.N4}, {latest.N5}";
                    }
                    StatusMessage = $"Updated — {_dataFantasy.Count} F5 · {_dataSuperLotto.Count} SL · {_dataDaily3.Count} D3 draws";
                });
            }
            catch { }
        }

        public async Task RefreshAllDataAsync()
        {
            IsLoading = true;
            StatusMessage = "Refreshing all data from CA Lottery...";
            try
            {
                await GetDataEntry.UpdateF5CsvAsync();
                await GetDataEntry.UpdateSLCsvAsync();
                await GetDataEntry.UpdateD3CsvAsync();
                await LoadFromFileAsync();
                await LoadSLFromFileAsync();
                await LoadD3FromFileAsync();
                if (_dataFantasy.Count > 0)
                {
                    var latest = _dataFantasy[0];
                    LastNumberHit = $"[{latest.DrawDate}] {latest.N1}, {latest.N2}, {latest.N3}, {latest.N4}, {latest.N5}";
                }
                StatusMessage = $"Refreshed — {_dataFantasy.Count} F5 · {_dataSuperLotto.Count} SL · {_dataDaily3.Count} D3 draws";
            }
            catch (Exception ex)
            {
                StatusMessage = "Refresh error: " + ex.Message;
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadFromFileAsync()
        {
            try
            {
                var draws = await GetDataEntry.LoadF5CsvDraws();
                _dataFantasy.Clear();
                foreach (var (drawDate, drawNum, nums, _) in draws)
                {
                    // drawDate is "ddd MMM d, yyyy" — split to get Month/Year
                    var parts = drawDate.Split(' ');
                    string month = parts.Length >= 2 ? parts[1] : "";
                    string year  = parts.Length >= 1 ? parts[^1] : "";
                    _dataFantasy.Add(new ModelDaily
                    {
                        DrawNumber = drawNum > 0 ? drawNum.ToString() : "",
                        DrawDate   = drawDate,
                        Month      = month,
                        Year       = year,
                        N1 = nums[0], N2 = nums[1], N3 = nums[2], N4 = nums[3], N5 = nums[4]
                    });
                }
            }
            catch { }
        }

        private async Task LoadSLFromFileAsync()
        {
            try
            {
                var draws = await GetDataEntry.LoadSLCsvDraws();
                _dataSuperLotto.Clear();
                foreach (var (drawDate, drawNum, mainNums, mega, _) in draws)
                {
                    var parts = drawDate.Split(' ');
                    string month = parts.Length >= 2 ? parts[1] : "";
                    string year  = parts.Length >= 1 ? parts[^1] : "";
                    _dataSuperLotto.Add(new ModelDaily
                    {
                        DrawNumber = drawNum > 0 ? drawNum.ToString() : "",
                        DrawDate   = drawDate,
                        Month      = month,
                        Year       = year,
                        N1 = mainNums[0], N2 = mainNums[1], N3 = mainNums[2],
                        N4 = mainNums[3], N5 = mainNums[4],
                        N6 = mega
                    });
                }
            }
            catch { }
        }

        private async Task LoadD3FromFileAsync()
        {
            try
            {
                var draws = await GetDataEntry.LoadD3CsvDraws();
                _dataDaily3.Clear();
                foreach (var (drawDate, drawNum, nums, drawTime) in draws)
                {
                    var parts = drawDate.Split(' ');
                    string month = parts.Length >= 2 ? parts[1] : "";
                    string year  = parts.Length >= 1 ? parts[^1] : "";
                    string label = string.IsNullOrEmpty(drawTime) ? drawDate : $"{drawDate} ({drawTime})";
                    _dataDaily3.Add(new ModelDaily
                    {
                        DrawNumber   = drawNum > 0 ? drawNum.ToString() : "",
                        DrawDate     = label,
                        Month        = month,
                        Year         = year,
                        N1 = nums[0], N2 = nums[1], N3 = nums[2],
                        DisplayCount = 3
                    });
                }
            }
            catch { }
        }

        // Returns (boxes, maxNum, howMany) parsed from header lines, or null if no file/headers.
        public async Task<(string[] Boxes, string MaxNum, string HowMany)?> LoadSavedCombosAsync()
        {
            try
            {
                var path = Path.Combine(FileSystem.AppDataDirectory, "myCombos.txt");
                if (!File.Exists(path)) return null;
                var lines = await File.ReadAllLinesAsync(path);

                string[]? boxes = null;
                string? maxNum = null, howMany = null;
                var combos = new List<string>();

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (line.StartsWith("#boxes:"))
                        boxes = line[7..].Split(',');
                    else if (line.StartsWith("#params:"))
                    {
                        var parts = line[8..].Split(',');
                        if (parts.Length >= 2) { maxNum = parts[0]; howMany = parts[1]; }
                    }
                    else
                        combos.Add(line);
                }

                if (combos.Count == 0) return null;
                Combinations = new ObservableCollection<string>(combos);
                NumberInList = combos.Count;
                StatusMessage = $"Loaded {combos.Count:N0} saved combos";

                if (boxes != null && maxNum != null && howMany != null)
                    return (boxes, maxNum, howMany);
            }
            catch { }
            return null;
        }

        public async Task LoadPicksAsync(int mode)
        {
            try
            {
                var fileName = mode == 1 ? "myNumbersSL.txt"
                             : mode == 2 ? "myNumberD3.txt"
                             :             "myNumbers.txt";
                using var stream = await FileSystem.OpenAppPackageFileAsync(fileName);
                using var reader = new StreamReader(stream);
                var content = await reader.ReadToEndAsync();
                var lines = content.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                Picks = new ObservableCollection<string>(lines);
            }
            catch { }
        }

        public double UpdateCombinations(int maxNum, int howMany)
        {
            ComboCalc.MaxNum = maxNum;
            ComboCalc.HowMany = howMany;
            PossibleCombinations = _calc.GetFact();
            return PossibleCombinations;
        }

        public List<string> CalculateCombinations(int[] mNum, int maxNum, int howMany)
        {
            ComboCalc.MaxNum = maxNum;
            ComboCalc.HowMany = howMany;
            ComboCalc.Opt = 1;
            _calc.GetFact();
            var results = _calc.CalcComb(mNum);
            Combinations = new ObservableCollection<string>(results);
            NumberInList = results.Count;
            return results;
        }

        // Pure compute — safe to call from Task.Run (no UI/property updates)
        public List<string> ComputeCombinationsAsync(int[] mNum, int maxNum, int howMany, Action<int>? onProgress = null)
        {
            ComboCalc.MaxNum = maxNum;
            ComboCalc.HowMany = howMany;
            ComboCalc.Opt = 1;
            _calc.GetFact();
            return _calc.CalcComb(mNum, onProgress);
        }

        public async Task LoadDrawsAsync()
        {
            IsLoading = true;
            StatusMessage = "Loading draws...";
            try
            {
                var draws = await GetDataEntry.LoadF5CsvDraws(); // newest-first
                var lines = draws.Select(d =>
                {
                    string dn = d.DrawNumber > 0 ? d.DrawNumber.ToString().PadLeft(5) : "     ";
                    string nums = string.Join(" ", d.Numbers.Select(n => n.ToString().PadLeft(2)));
                    return $"{dn}  {d.DrawDate,-20}  {nums}";
                }).ToList();
                DrawsHistory = new ObservableCollection<string>(lines);
                NumberInList = lines.Count;
                StatusMessage = $"Complete — {lines.Count} draws loaded";
            }
            catch (Exception ex)
            {
                StatusMessage = "Error loading draws: " + ex.Message;
            }
            finally
            {
                IsLoading = false;
            }
        }

        public void SearchRecurrence(string numbers, string matchCount)
        {
            var results = MatchParsing.ExactRecurrence(numbers, _dataFantasy, matchCount).ToList();
            RecurrenceResults = new ObservableCollection<ModelDaily>(results);
            NumberInList = results.Count;
        }

        public void SearchRecurrenceD3(string numbers, string matchCount)
        {
            var results = MatchParsing.ExactRecurrenceD3(numbers, _dataDaily3, matchCount).ToList();
            RecurrenceResults = new ObservableCollection<ModelDaily>(results);
            NumberInList = results.Count;
        }

        public void SearchRecurrenceSL(string numbers, string matchCount)
        {
            var results = MatchParsing.ExactRecurrenceSL(numbers, _dataSuperLotto, matchCount).ToList();
            RecurrenceResults = new ObservableCollection<ModelDaily>(results);
            NumberInList = results.Count;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
