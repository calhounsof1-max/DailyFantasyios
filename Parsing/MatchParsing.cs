using DailyFantasyMAUI.Model;
using System.Collections.ObjectModel;

namespace DailyFantasyMAUI.Parsing
{
    public static class MatchParsing
    {
        public static IEnumerable<ModelDaily> ExactRecurrence(string numbers, ObservableCollection<ModelDaily> data, string selectedCount)
        {
            var rc = new List<ModelDaily>();
            var numSplit = numbers.Split(new char[] { '\r', '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var nm in data)
            {
                var currentNum = $"{nm.N1} {nm.N2} {nm.N3} {nm.N4} {nm.N5}";
                for (int i = 0; i < numSplit.Length; i++)
                {
                    if (RecurrenceMatch(currentNum, numSplit[i], selectedCount))
                    {
                        rc.Add(nm);
                        break;
                    }
                }
            }

            return rc;
        }

        // Single number set vs draw — count matches
        private static bool RecurrenceMatch(string currentNumber, string number, string selectCount)
        {
            var numSplit = number.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var curSplit = currentNumber.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int count = 0;

            for (int i = 0; i < numSplit.Length && i < 5; i++)
                for (int j = 0; j < curSplit.Length && j < 5; j++)
                    if (numSplit[i] == curSplit[j]) count++;

            return count == int.Parse(selectCount);
        }

        // Match a 5-number line against a draw
        public static bool CheckFiveNumbers(string drawNumbers, string userNumbers, out string matched)
        {
            matched = string.Empty;
            var numSplit = userNumbers.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var curSplit = drawNumbers.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int count = 0;
            var matchList = new List<string>();

            for (int i = 0; i < Math.Min(numSplit.Length, 5); i++)
                for (int j = 0; j < Math.Min(curSplit.Length, 5); j++)
                    if (numSplit[i] == curSplit[j]) { matchList.Add(numSplit[i]); count++; }

            matched = string.Join(" ", matchList);
            return count > 1;
        }

        public static int[] FrequencyHits(ObservableCollection<ModelDaily> data)
        {
            int[] arr = new int[40];
            foreach (var f in data)
            {
                if (f.N1 < 40) arr[f.N1]++;
                if (f.N2 < 40) arr[f.N2]++;
                if (f.N3 < 40) arr[f.N3]++;
                if (f.N4 < 40) arr[f.N4]++;
                if (f.N5 < 40) arr[f.N5]++;
            }
            return arr;
        }
    }
}
