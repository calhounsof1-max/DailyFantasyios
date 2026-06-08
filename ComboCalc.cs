namespace DailyFantasyMAUI
{
    public class ComboCalc
    {
        public static int Opt = 0, Matches = 0, Selections = 0;
        public static double TotCombinations;
        public static int MaxNum, HowMany;

        public List<string> CalcComb(int[] mNum, Action<int>? onProgress = null)
        {
            var results = new List<string>();
            int x = 0, y = 0, z = 0, count = 0, countcombinations = 0;
            int[] a = new int[20];
            string? strNum = null;
            int firstentry = 0;

            do
            {
                if (firstentry == 0)
                {
                    x = 0; y = Matches; firstentry = 1;
                    countcombinations = 0;
                }
                else
                {
                    for (y = 1; y < Matches + 1; y++)
                    {
                        x = a[Matches + 1 - y];
                        if (x != Selections + 1 - y) break;
                    }
                }
                for (z = 1; z < y + 1; z++)
                    a[Matches + z - y] = x + z;

                countcombinations++;

                if (Opt == 1)
                {
                    strNum = null;
                    for (count = 1; count < Matches + 1; count++)
                        strNum += mNum[a[count]].ToString() + " ";
                    if (strNum != null) results.Add(strNum.Trim());
                }

                if (onProgress != null && countcombinations % 5000 == 0)
                    onProgress(countcombinations);

            } while (countcombinations < (long)TotCombinations);

            return results;
        }

        public double CalcPerm(int total, int pselect)
        {
            if (total == 0 || pselect == 0) return 0;
            double diff = total - pselect;
            return Ffact(total) / (Ffact(pselect) * Ffact(diff));
        }

        public double Ffact(double pnumber)
        {
            double result = 1;
            for (double f = pnumber; f > 1; f--)
                result *= f;
            return result;
        }

        public double GetFact()
        {
            Matches = MaxNum;
            Selections = HowMany;
            TotCombinations = Math.Round(CalcPerm(HowMany, MaxNum));
            return TotCombinations;
        }
    }
}
