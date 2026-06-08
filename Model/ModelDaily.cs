using System.ComponentModel;

namespace DailyFantasyMAUI.Model
{
    public class ModelDaily : INotifyPropertyChanged
    {
        private int n1, n2, n3, n4, n5;
        private string _drawDate = "", _drawNumber = "", _month = "", _year = "";

        public string Month { get => _month; set { _month = value; OnPropertyChanged(nameof(Month)); } }
        public string Year { get => _year; set { _year = value; OnPropertyChanged(nameof(Year)); } }
        public string DrawNumber { get => _drawNumber; set { _drawNumber = value; OnPropertyChanged(nameof(DrawNumber)); } }
        public string DrawDate { get => _drawDate; set { _drawDate = value; OnPropertyChanged(nameof(DrawDate)); } }
        public int N1 { get => n1; set { n1 = value; OnPropertyChanged(nameof(N1)); } }
        public int N2 { get => n2; set { n2 = value; OnPropertyChanged(nameof(N2)); } }
        public int N3 { get => n3; set { n3 = value; OnPropertyChanged(nameof(N3)); } }
        public int N4 { get => n4; set { n4 = value; OnPropertyChanged(nameof(N4)); } }
        public int N5 { get => n5; set { n5 = value; OnPropertyChanged(nameof(N5)); } }

        public string Display => $"{N1,2} {N2,2} {N3,2} {N4,2} {N5,2}   {DrawDate}";

        public event PropertyChangedEventHandler? PropertyChanged;
        public void OnPropertyChanged(string pc) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(pc));
    }
}
