namespace BecquerelMonitor
{
    public class AtomSpectraDeviceConfig : InputDeviceConfig
    {
        string com_port_name;
        int baud_rate = 600000;
        double deadTime = 0;

        public string ComPortName
        {
            get { return com_port_name; }
            set { this.com_port_name = value; }
        }

        public int BaudRate
        {
            get { return baud_rate; }
            set {baud_rate = value; }
        }

        public double DeadTimeValue
        {
            get { return deadTime; }
            set { this.deadTime = value; }
        }

        public AtomSpectraDeviceConfig()
        {

        }

        public AtomSpectraDeviceConfig(AtomSpectraDeviceConfig instance)
        {
            this.com_port_name = instance.com_port_name;
            this.baud_rate = instance.baud_rate;
            this.deadTime = instance.deadTime;
        }

        public override InputDeviceConfig Clone()
        {
            return new AtomSpectraDeviceConfig(this);
        }

        public override double DeadTime()
        {
            return this.deadTime;
        }

        /// <summary>
        /// ⛔ `AMBER127` (П170, 28.09.2026): МЁРТВОЕ ВРЕМЯ — ИЗ ОТВЕТА ПРИБОРА
        /// НА `-inf`, ПО ИМЕНАМ ПАРАМЕТРОВ, А НЕ ПО НОМЕРУ СЛОВА.
        ///
        /// Ответ — пары «ИМЯ значение» через пробел, например
        /// «VERSION 13 RISE 8 FALL 12 NOISE 15 F 3000000.00 MAX 30000 …»
        /// (протокол Atom Spectra: RISE и FALL — фронт и спад импульса в
        /// отсчётах АЦП, F — частота оцифровки, Гц). Кнопка формы брала слова
        /// [3], [5], [9] после разбиения по одному пробелу: лишний пробел или
        /// перевод строки в ответе сдвигали их молча, а отказ глотал пустой
        /// <c>catch</c>. τ = (RISE + FALL + 1) / F — формула прежней кнопки
        /// (импульс занимает RISE + FALL + 1 отсчёт); сама программа AtomSpectra
        /// мёртвого времени не возмещает вовсе и пишет LT = T.
        ///
        /// Отказ — <see cref="double.NaN"/> и причина словами в
        /// <paramref name="error"/> (нет параметра, не число, F ≤ 0); ответ
        /// никогда не подменяется нулём.
        /// </summary>
        public static double DeadTimeFromInfo(string answer, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(answer))
            {
                error = "the device gave no answer to -inf";
                return double.NaN;
            }
            string[] words = answer.Split(new[] { ' ', '\t', '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
            double rise = double.NaN, fall = double.NaN, frequency = double.NaN;
            for (int i = 0; i + 1 < words.Length; i++)
            {
                double value;
                string key = words[i];
                if (key != "RISE" && key != "FALL" && key != "F")
                {
                    continue;
                }
                if (!double.TryParse(words[i + 1], System.Globalization.NumberStyles.Float,
                                     System.Globalization.CultureInfo.InvariantCulture, out value))
                {
                    error = "-inf: parameter " + key + " is not a number (\"" + words[i + 1] + "\")";
                    return double.NaN;
                }
                if (key == "RISE") rise = value;
                else if (key == "FALL") fall = value;
                else frequency = value;
                i++;
            }
            if (double.IsNaN(rise) || double.IsNaN(fall) || double.IsNaN(frequency))
            {
                error = "-inf: no " + (double.IsNaN(rise) ? "RISE" : double.IsNaN(fall) ? "FALL" : "F")
                        + " in the answer \"" + (answer.Length > 80 ? answer.Substring(0, 80) + "…" : answer).Trim() + "\"";
                return double.NaN;
            }
            if (!(frequency > 0.0) || rise < 0.0 || fall < 0.0 || double.IsInfinity(frequency))
            {
                error = "-inf: RISE " + rise.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + ", FALL " + fall.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + ", F " + frequency.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + " do not give a dead time";
                return double.NaN;
            }
            return (rise + fall + 1.0) / frequency;
        }
    }
}
