using System;
using System.Globalization;

namespace MassaKWin.Core
{
    public static class WeightFormatter
    {
        public static string FormatWeight(double grams, WeightUnit unit, int decimals)
        {
            var value = ConvertWeight(grams, unit);
            var format = "F" + Math.Max(0, decimals);
            return value.ToString(format, CultureInfo.InvariantCulture);
        }

        public static double ConvertWeight(double grams, WeightUnit unit)
        {
            return unit == WeightUnit.Kg ? grams / 1000.0 : grams;
        }
    }
}
