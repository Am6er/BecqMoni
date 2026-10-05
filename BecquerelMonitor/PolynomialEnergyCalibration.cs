using BecquerelMonitor.Properties;
using MathNet.Numerics;
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
    // Token: 0x02000094 RID: 148
    public class PolynomialEnergyCalibration : EnergyCalibration
    {
        // Token: 0x17000218 RID: 536
        // (get) Token: 0x0600072C RID: 1836 RVA: 0x00029CF0 File Offset: 0x00027EF0
        // (set) Token: 0x0600072D RID: 1837 RVA: 0x00029CF8 File Offset: 0x00027EF8
        public int PolynomialOrder
        {
            get
            {
                return this.polynomialOrder;
            }
            set
            {
                this.polynomialOrder = value;
                this.dirty = true;
            }
        }

        // Token: 0x17000219 RID: 537
        // (get) Token: 0x0600072E RID: 1838 RVA: 0x00029D04 File Offset: 0x00027F04
        // (set) Token: 0x0600072F RID: 1839 RVA: 0x00029D0C File Offset: 0x00027F0C
        [XmlArrayItem("Coefficient")]
        public double[] Coefficients
        {
            get
            {
                return this.coefficients;
            }
            set
            {
                this.coefficients = value;
                this.dirty = true;
            }
        }

        public PolynomialEnergyCalibration()
        {
            this.polynomialOrder = 1;
            double[] array = new double[2];
            array[1] = 1.0;
            this.coefficients = array;
        }

        // Token: 0x06000731 RID: 1841 RVA: 0x00029D50 File Offset: 0x00027F50
        public PolynomialEnergyCalibration(PolynomialEnergyCalibration calib)
        {
            this.polynomialOrder = calib.polynomialOrder;
            this.coefficients = (double[])calib.coefficients.Clone();
            this.maxEnergy = calib.maxEnergy;
            this.maxChannels = calib.maxChannels;
        }

        // Token: 0x06000732 RID: 1842 RVA: 0x00029D7C File Offset: 0x00027F7C
        public override bool Equals(EnergyCalibration calib)
        {
            PolynomialEnergyCalibration polynomialEnergyCalibration = (PolynomialEnergyCalibration)calib;
            if (this.polynomialOrder != polynomialEnergyCalibration.polynomialOrder)
            {
                return false;
            }
            for (int i = 0; i <= this.polynomialOrder; i++)
            {
                if (this.coefficients[i] != polynomialEnergyCalibration.coefficients[i])
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Годна ли шкала: степень, длина набора коэффициентов, монотонность и
        /// разумность энергий по всем каналам. Ответ читают ВСЕ 23 места
        /// дерева, где шкала берётся из файла или из подгонки, — импорт N42,
        /// открытие документа, сохранение конфигурации прибора, вычитание фона,
        /// стабилизатор пиков, график калибровки.
        ///
        /// ⛔ ЗАСЛОН ОТ НЕ-ЧИСЕЛ СТОИТ ЗДЕСЬ, А НЕ У ВЫЗЫВАЮЩИХ (полоса F48,
        /// 06.09.2026; найдено полосой F44). Измерено на собранном коде:
        /// <c>[NaN, NaN]</c> и <c>[NaN, +∞]</c> эта проверка объявляла ГОДНЫМИ.
        /// Причина в самом языке: любое сравнение с <c>NaN</c> ложно, поэтому
        /// ни <c>Coefficients[1] == 0</c>, ни <c>prevEnrg &gt;= 100000.0</c>,
        /// ни <c>prevEnrg &gt; ChannelToEnergy(i)</c> не срабатывают, и цикл
        /// проходит насквозь. Человек получал это двумя точками калибровки на
        /// ОДНОМ канале: решатель отдаёт вырожденной матрице <c>NaN</c>,
        /// «Рассчитать» молча принимало шкалу, конфигурация метилась
        /// изменённой и <c>NaN</c> уезжал в файл прибора.
        ///
        /// Место выбрано счётом, а не вкусом: коэффициенты приходят из файла
        /// или из подгонки у ВСЕХ 23 вызывающих, то есть заслон у вызывающих
        /// пришлось бы ставить 23 раза и держать в согласии; и ни одному из
        /// них шкала, отображающая каждый канал в <c>NaN</c>, не годна — стало
        /// быть неверен ОТВЕТ, а не места, где его спрашивают.
        ///
        /// ⚠ Заслон стоит ПОСЛЕ строк, считающих <c>maxChannels</c> и
        /// <c>maxEnergy</c>: их побочное действие нужно тем вызывающим, кто
        /// зовёт проверку ради него и ответ отбрасывает
        /// (<c>EfficiencyCurveIo.LoadResultData</c>, пробы), и на годном входе
        /// поведение обязано остаться прежним до бита.
        /// </summary>
        public bool CheckCalibration(int channels = 8192)
        {
            this.maxChannels = channels;
            if (this.maxEnergy == -1 || this.dirty) { this.maxEnergy = this.ChannelToEnergy(this.maxChannels); }

            // Проверяются ВСЕ коэффициенты набора, а не только участвующие в
            // текущей степени: длина набора у степеней 1..4 закреплена ниже
            // (`Coefficients.Length != order + 1` — отказ), так что разницы
            // нет, а набор, пришедший из файла с лишним `NaN` в хвосте,
            // остаётся отвергнутым и после `Downgrade`.
            if (this.coefficients != null)
            {
                for (int i = 0; i < this.coefficients.Length; i++)
                {
                    if (double.IsNaN(this.coefficients[i]) || double.IsInfinity(this.coefficients[i]))
                    {
                        return false;
                    }
                }
            }

            if (this.polynomialOrder == 1)
            {
                if (this.Coefficients.Length != 2 || this.Coefficients[1] == 0)
                {
                    return false;
                }
            }
            if (this.polynomialOrder == 2)
            {
                if (this.Coefficients.Length != 3 || this.Coefficients[2] == 0)
                {
                    return false;
                }
                double c = this.Coefficients[0];
                double b = this.Coefficients[1];
                double a = this.Coefficients[2];
                double discriminant = Math.Pow(b, 2.0) - 4.0 * a * c;

                // ⛔ `AMBER190` (полоса fixcal, 05.10.2026): здесь стояло второе
                // условие `b² − 4a(c − channels) < 0` — «у параболы есть корень при
                // E = (число каналов) кэВ». Родилось 07.07.2023 (`3e69dfc3`) как
                // «шкала достаёт до 12 МэВ» (12000.0), 30.07.2023 (`a5b78ebe`)
                // константа заменена числом КАНАЛОВ — смешение единиц. После зажима
                // `EnergyToChannel` (энергия выше E(N) → канал N) корень у
                // E = channels не нужен никому, а отвергало условие годные вогнутые
                // шкалы: c = 0, b = 0.40, a = −5e-6, N = 8192 (изгиб 10 %,
                // монотонна) — «Calibration function error», молчаливое понижение
                // степени в `SolveGuarded`, сброс шкалы при открытии файла.
                // Монотонность на [0, N] проверяет цикл ниже.
                if (discriminant < 0)
                {
                    return false;
                }
            }
            if (this.polynomialOrder == 3)
            {
                if (this.Coefficients.Length != 4 || this.Coefficients[3] == 0)
                {
                    return false;
                }
            }
            if (this.polynomialOrder == 4)
            {
                if (this.Coefficients.Length != 5 || this.Coefficients[4] == 0)
                {
                    return false;
                }
            }
            if (this.polynomialOrder > 4 || this.polynomialOrder < 1) { return false; }
            for (int i = 1; i <= channels; i++)
            {
                double prevEnrg = this.ChannelToEnergy(i - 1);
                if (prevEnrg <= -1000.0 || prevEnrg >= 100000.0) return false;
                if (prevEnrg > this.ChannelToEnergy(i))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// ⛔ `AMBER188(д)` (полоса fixcal, 05.10.2026): ввод ОДНОГО коэффициента
        /// шкалы в поле — общий помощник панели калибровки энергии
        /// (<c>DCEnergyCalibrationView.setNewCalibration</c>) и формы прибора
        /// (<c>DeviceConfigForm.setNewCalibration</c>); обе ведут себя одинаково.
        ///
        /// Решение Amber 05.10.2026, вопросником, дословно: «Подъём до введённой
        /// (Рекомендую)». То есть:
        /// <list type="bullet">
        /// <item>ненулевой коэффициент при xᵏ у шкалы степени ниже k поднимает
        /// степень до k, промежуточные коэффициенты — нули (прежде массив рос на
        /// одну ступень, и ввод x⁴ у линейной шкалы падал
        /// <c>IndexOutOfRange</c>);</item>
        /// <item>ноль (ЧИСЛЕННО, а не текст «0» — «0.0» тоже ноль) в старшем
        /// коэффициенте понижает степень до СТАРШЕГО НЕНУЛЕВОГО: у
        /// [c, b, 0, d₃, d₄] ноль в x⁴ даёт степень 3, у [c, b, a, 0, d₄] —
        /// степень 2.</item>
        /// </list>
        ///
        /// Возвращает НОВЫЙ объект; исходный не трогается — отказ у вызывающего
        /// не оставляет живую шкалу в промежуточном состоянии. Годность шкалы
        /// (<see cref="CheckCalibration"/>) помощник НЕ проверяет: панель
        /// проверяет сразу, форма прибора — при сохранении. Бросает
        /// <see cref="ArgumentException"/> на степени вне 0…4, на не-числе и на
        /// шкале, где все коэффициенты при степенях x равны нулю (энергия не
        /// зависит от канала — такую не примет ни одна проверка).
        /// </summary>
        public static PolynomialEnergyCalibration WithCoefficient(PolynomialEnergyCalibration source, int order, double value)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (order < 0 || order > 4)
            {
                throw new ArgumentOutOfRangeException(nameof(order), order, "coefficient index must be 0..4");
            }
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentException("coefficient must be a finite number", nameof(value));
            }

            // Только участвующие коэффициенты исходной шкалы: хвост массива за
            // её степенью (если он есть) в новую шкалу не переносится.
            int sourceTop = Math.Min(source.polynomialOrder, source.coefficients.Length - 1);
            double[] work = new double[Math.Max(sourceTop, order) + 1];
            Array.Copy(source.coefficients, work, sourceTop + 1);
            work[order] = value;

            int degree = work.Length - 1;
            while (degree >= 1 && work[degree] == 0.0)
            {
                degree--;
            }
            if (degree < 1)
            {
                throw new ArgumentException("all channel-dependent coefficients are zero: energy would not depend on the channel", nameof(value));
            }

            double[] coefficients = new double[degree + 1];
            Array.Copy(work, coefficients, degree + 1);

            PolynomialEnergyCalibration result = (PolynomialEnergyCalibration)source.Clone();
            result.PolynomialOrder = degree;
            result.Coefficients = coefficients;
            return result;
        }

        public override EnergyCalibration Downgrade(int polynomialOrder)
        {
            PolynomialEnergyCalibration p = (PolynomialEnergyCalibration)this.Clone();
            p.PolynomialOrder = polynomialOrder;
            double[] c = new double[polynomialOrder + 1];
            for (int i = 0; i < c.Length; i++)
            {
                c[i] = this.Coefficients[i];
            }
            p.Coefficients = c;
            return p;
        }

        // Token: 0x06000733 RID: 1843 RVA: 0x00029DD4 File Offset: 0x00027FD4
        public override double ChannelToEnergy(double n)
        {
            if (n < 0) n = 0;
            if (n > this.maxChannels) n = this.maxChannels;
            if (this.polynomialOrder == 4)
            {
                return this.coefficients[4] * Math.Pow(n, 4) + this.coefficients[3] * Math.Pow(n, 3) + this.coefficients[2] * Math.Pow(n, 2) + this.coefficients[1] * n + this.coefficients[0];
            }
            if (this.polynomialOrder == 3)
            {
                return this.coefficients[3] * Math.Pow(n, 3) + this.coefficients[2] * Math.Pow(n, 2) + this.coefficients[1] * n + this.coefficients[0];
            }
            if (this.polynomialOrder == 2)
            {
                return this.coefficients[2] * Math.Pow(n, 2) + this.coefficients[1] * n + this.coefficients[0];
            }
            // Степени выше четвёртой: раньше они молча проваливались в линейную
            // ветку ниже. Спектр с калибровкой 5-й степени (их пишет SpectraLine
            // ЛСРМ) открывался с неверной шкалой по всему диапазону и без единого
            // сообщения. Отбрасывание старшего члена не спасает: на канале 8192
            // ошибка 53 кэВ на германии и более 14 000 кэВ на изогнутой
            // NaI-калибровке. Считаем схемой Горнера по всем коэффициентам,
            // которые реально есть.
            if (this.polynomialOrder > 4)
            {
                int top = Math.Min(this.polynomialOrder, this.coefficients.Length - 1);
                double value = 0.0;
                for (int i = top; i >= 0; i--)
                {
                    value = value * n + this.coefficients[i];
                }
                return value;
            }
            return this.coefficients[1] * n + this.coefficients[0];
        }

        // Token: 0x06000734 RID: 1844 RVA: 0x00029E1C File Offset: 0x0002801C
        // Writes of the form calibration.Coefficients[i] = x mutate the array through the
        // getter and bypass the property setter, so the dirty flag (and with it the
        // EnergyToChannel cache) was never invalidated. Call this after such writes.
        public void InvalidateCache()
        {
            this.dirty = true;
        }

        // Потолок мемоизации энергия->канал. С запасом перекрывает пиксельную
        // ширину графика на любом мониторе, так что при фиксированном масштабе
        // сброс не срабатывает вовсе.
        const int EnergyToChannelCacheLimit = 32768;

        public override double EnergyToChannel(double enrg, int maxCh = 8192)
        {
            // Changing maxCh must also drop the cached energy->channel map, not only
            // maxEnergy - stale entries from the previous maxChannels survived here.
            if (this.maxChannels != maxCh) { this.maxChannels = maxCh; this.maxEnergy = -1; this.dirty = true; }
            if (this.maxEnergy == -1 || this.dirty) this.maxEnergy = ChannelToEnergy(this.maxChannels);
            if (enrg > this.maxEnergy && this.maxEnergy != -1) return this.maxChannels;
            if (this.dirty || this.energytochanel == null)
            {
                EnergyToChannelMemo fresh = new EnergyToChannelMemo();
                this.energytochanel = fresh;
                this.dirty = false;
                double value = EnrgToChannel(enrg, maxCh: this.maxChannels);
                fresh.Add(enrg, value);
                return value;
            } else
            {
                EnergyToChannelMemo memo = this.energytochanel;
                if (memo.Map.TryGetValue(enrg, out double value))
                {
                    return value;
                } else
                {
                    value = EnrgToChannel(enrg, maxCh: this.maxChannels);
                    // Кеш чисто мемоизационный, но раньше он не имел потолка и
                    // рос неограниченно: график зовёт EnergyToChannel по одному
                    // ключу на пиксель, и при масштабировании КАЖДЫЙ кадр даёт
                    // новый набор энергий - за 40 шагов зума набегало под 50 тыс.
                    // записей, которые больше никогда не понадобятся. Сброс
                    // безопасен: это чистая функция, пересчёт дешёвый.
                    //
                    // `T267` (П213, 02.10.2026): потолок сверяется со СЧЁТЧИКОМ
                    // записей рядом со словарём, а не с `ConcurrentDictionary.Count`:
                    // тот берёт ВСЕ замки словаря (4 на ядро) на каждом промахе, а в
                    // разборе FSA почти каждый вызов — промах: 159 из 183 мс выборок
                    // места, ≈2.5 % разбора `AS80_Th232Medal` (профиль П213 на
                    // `5b6a209c`). Поведение при переполнении прежнее: на промахе
                    // при 32768 записях словарь очищается, затем кладётся новая.
                    if (memo.Count >= EnergyToChannelCacheLimit)
                    {
                        memo.Clear();
                    }
                    memo.Add(enrg, value);
                    return value;
                }
            }
        }

        /// <summary>
        /// `T267` (П213): словарь мемоизации энергия → канал вместе со счётчиком
        /// своих записей. Счётчик живёт В ТОМ ЖЕ объекте, что и словарь, — замена
        /// словаря при смене шкалы (`dirty`) меняет их разом, одной записью
        /// ссылки, и окна «новый словарь, старый счёт» нет.
        ///
        /// Без гонки счёт точный (растёт только на удавшемся <c>TryAdd</c>,
        /// обнуляется вместе с <c>Clear</c>). Под гонкой — приблизительный: запись
        /// другого потока между <c>Clear</c> и обнулением счёта может не попасть
        /// в счёт, и словарь перерастёт потолок на число одновременных потоков;
        /// значения от этого не меняются — кеш хранит чистую функцию. Сам словарь
        /// остаётся <c>ConcurrentDictionary</c>: к одной калибровке ходят и
        /// отрисовка, и <c>Task.Run</c> поиска пиков и FSA.
        /// </summary>
        sealed class EnergyToChannelMemo
        {
            public readonly ConcurrentDictionary<double, double> Map = new ConcurrentDictionary<double, double>();

            int count;

            public int Count
            {
                get { return Volatile.Read(ref this.count); }
            }

            public void Add(double energy, double channel)
            {
                if (this.Map.TryAdd(energy, channel))
                {
                    Interlocked.Increment(ref this.count);
                }
            }

            public void Clear()
            {
                this.Map.Clear();
                Interlocked.Exchange(ref this.count, 0);
            }
        }

        /// <summary>
        /// ⛔ Калибровка непригодна: обратного отображения энергии в канал у
        /// неё НЕТ. Здесь стояли два голых <c>MessageBox.Show</c>, и они
        /// ВЕШАЛИ БЕЗОКОННЫЙ ПРОГОН (`S100`) — измерено 27.08.2026 на
        /// собранном коде, плечи <c>poly-degenerate</c> и
        /// <c>poly-discriminant</c>: процесс убит по сроку 20 с, класс окна
        /// <c>#32770</c>, заголовок пуст.
        ///
        /// ⛔ Без окон это ОТКАЗ, а не строка в поток ошибок, и вот почему.
        /// Оба места кончаются <c>return 0</c>, то есть «канал ноль» — и
        /// вызывающий не отличает его от честного нуля: сюда ходят и разметка
        /// линий библиотеки, и поиск пиков, и полноспектральное разложение.
        /// Молча продолжив, прогон сложит всю библиотеку в нулевой канал и
        /// выдаст ЧИСЛА, а числа с непригодной калибровкой — не «хуже», а
        /// чужие. Читатель у отказа — код возврата пробы.
        ///
        /// ⚠ Ухудшения по сравнению с прежним поведением тут быть не может:
        /// любой вход, который сегодня бросает, вчера ВИСЕЛ НАСМЕРТЬ. В окнах
        /// всё как было — то же модальное окно и тот же <c>return 0</c>.
        ///
        /// ⚠ Заголовок окна: прежде его не было вовсе (<c>MessageBox.Show</c>
        /// об одном доводе), теперь общий <c>ErrorDialogTitle</c> — как у
        /// прочих сообщений, идущих через <see cref="AppUi"/>.
        /// </summary>
        void UnusableCalibration(string why, double enrg)
        {
            if (!AppUi.HasWindows)
            {
                throw new InvalidOperationException(
                    "BecqMoni: the energy calibration cannot be inverted and there is no UI to report it to: "
                    + why + "; requested energy " + enrg.ToString("R", CultureInfo.InvariantCulture) + " keV, coefficients ["
                    + string.Join(", ", Array.ConvertAll(this.coefficients, v => v.ToString("R", CultureInfo.InvariantCulture)))
                    + "]. Continuing would return channel 0, which is indistinguishable from an honest zero. "
                    + Resources.CalibrationFunctionError);
            }
            AppUi.Report(Resources.CalibrationFunctionError, Resources.ErrorDialogTitle,
                System.Windows.Forms.MessageBoxIcon.Hand);
        }

        /// <summary>
        /// ⛔ `S184` (П151, 24.09.2026): ЭНЕРГИЯ → КАНАЛ С ПРОДОЛЖЕНИЕМ ШКАЛЫ
        /// В [E(0), 0). <see cref="EnergyToChannel"/> отдаёт канал 0 всякой
        /// энергии ниже нуля, и при E(0) &lt; 0 это зажим ВНУТРИ шкалы: полином
        /// даёт там каналы 0…k. Замер `EnergyScaleProbeP151`: у 118 из 125
        /// спектров корпуса с E(0) &lt; 0 круг «канал → энергия → канал» ошибался
        /// до 46 каналов; на графике в режиме энергии (`G1S16_Mn54_P5`,
        /// E(0) = −43.9 кэВ) 425 из 440 колонок в [E(0), 0) брали канал 0, и
        /// 14 каналов не рисовались вовсе.
        ///
        /// ⚠ ПОЧЕМУ ОТДЕЛЬНЫМ ВЫЗОВОМ, А НЕ В САМОЙ КАЛИБРОВКЕ. Проба той же
        /// правкой в <see cref="EnrgToChannel"/> сдвинула разбор FSA: витрина
        /// разошлась с эталоном на 3 парах из 9 (до −1.33 % у малой компоненты,
        /// в основном 1E-5) — продолжение FSA (`LightToChannel`, `AMBER85`) идёт
        /// прямой по ширине нулевого канала, и полином отличается от неё до
        /// 0.085 канала (`AS80_Onyx`). Разбор FSA держит своё продолжение, а
        /// вызывающие вне него (график, фон, сложение спектров, ROI) зовут это.
        ///
        /// Ниже E(0) и выше верха — как <see cref="EnergyToChannel"/> (канал 0 и
        /// N: номер канала — индекс массива у вызывающих). Вне [E(0), 0) ответ
        /// побитово тот же, что у <see cref="EnergyToChannel"/>. Калибровка не
        /// полиномиальная (<c>NonlinearEnergyCalibration</c>) — её собственный
        /// ответ как есть.
        /// </summary>
        public static double ChannelOf(EnergyCalibration calibration, double energy, int channels)
        {
            double channel = calibration.EnergyToChannel(energy, channels);
            PolynomialEnergyCalibration polynomial = calibration as PolynomialEnergyCalibration;
            if (polynomial == null || channel > 0.0 || !(energy < 0.0) || !(energy >= polynomial.coefficients[0]))
            {
                return channel;
            }

            return polynomial.EnrgToChannel(energy, maxCh: channels, clampBelowZero: false);
        }

        double EnrgToChannel(double enrg, int maxCh = 8192, bool clampBelowZero = true)
        {
            // `S184`: `enrg < 0` — зажим ВНУТРИ шкалы при E(0) < 0; снимает его
            // только <see cref="ChannelOf"/> (разбор FSA держит своё продолжение).
            if ((clampBelowZero && enrg < 0) || enrg < this.coefficients[0])
            {
                return 0;
            }

            if (this.polynomialOrder == 1)
            {
                double k = this.coefficients[1];
                double b = this.coefficients[0];
                if (k == 0) k = 1;
                return (enrg - b) / k;
            }

            if (this.polynomialOrder == 2)
            {
                double a = this.coefficients[2];
                double b = this.coefficients[1];
                double c = this.coefficients[0] - enrg;
                if (a == 0.0)
                {
                    if (b == 0.0)
                    {
                        this.UnusableCalibration("polynomial order 2 with both the quadratic and the linear "
                            + "coefficient equal to zero: energy does not depend on the channel at all", enrg);
                        return 0;
                    }
                    return - c / b;
                }
                else
                {
                    double discriminant = Math.Pow(b, 2.0) - 4.0 * a * c;
                    if (discriminant < 0.0)
                    {
                        this.UnusableCalibration("polynomial order 2, negative discriminant (" + discriminant.ToString("R", CultureInfo.InvariantCulture)
                            + "): NO channel of this calibration carries the requested energy", enrg);
                        return 0;
                    }
                    double sqrtD = Math.Sqrt(discriminant);
                    double root1 = (-b + sqrtD) / (2.0 * a);
                    double root2 = (-b - sqrtD) / (2.0 * a);
                    bool root1InRange = root1 >= 0.0 && root1 <= maxCh;
                    bool root2InRange = root2 >= 0.0 && root2 <= maxCh;
                    if (root1InRange && root2InRange) return Math.Min(root1, root2);
                    if (root1InRange) return root1;
                    if (root2InRange) return root2;
                    if (root1 >= 0.0) return root1;
                    if (root2 >= 0.0) return root2;
                    return 0;
                }
            }
            if (this.polynomialOrder == 4)
            {
                Func<double, double> f1 = x => this.coefficients[4] * x * x * x * x + this.coefficients[3] * x * x * x + this.coefficients[2] * x * x + this.coefficients[1] * x + this.coefficients[0] - enrg;
                try
                {
                    double roots = FindRoots.OfFunction(f1, 0, maxCh);
                    //System.Windows.Forms.MessageBox.Show("Calibration coefficients are incorrect channels for Energy: " + enrg + " roots = " + roots);
                    return roots;
                }
                catch
                {
                    //throw new Exception(String.Format("Calibration coefficients are incorrect channels for Energy: " + enrg));
                    //System.Windows.Forms.MessageBox.Show("Calibration coefficients are incorrect channels for Energy: " + enrg);
                    return 0;
                }
            }

            if (this.polynomialOrder == 3)
            {

                Func<double, double> f1 = x => this.coefficients[3] * x * x * x + this.coefficients[2] * x * x + this.coefficients[1] * x + this.coefficients[0] - enrg;
                try
                {
                    double roots = FindRoots.OfFunction(f1, 0, maxCh);
                    return roots;
                }
                catch
                {
                    //System.Windows.Forms.MessageBox.Show("Calibration coefficients are incorrect channels for Energy: " + enrg);
                    //throw new Exception(String.Format("Calibration coefficients are incorrect channels for Energy: " + enrg));
                    return 0;
                }
            }

            // Обратное преобразование для степеней выше четвёртой. Раньше здесь
            // летело исключение, а DetectPeak вызывается под catch-all в
            // DCPeakDetectionView — то есть на спектре с калибровкой 5-й степени
            // поиск пиков молча не работал вовсе. Корень ищем тем же способом,
            // что для 3-й и 4-й степени.
            if (this.polynomialOrder > 4)
            {
                int top = Math.Min(this.polynomialOrder, this.coefficients.Length - 1);
                Func<double, double> poly = x =>
                {
                    double value = 0.0;
                    for (int i = top; i >= 0; i--)
                    {
                        value = value * x + this.coefficients[i];
                    }
                    return value - enrg;
                };
                try
                {
                    return FindRoots.OfFunction(poly, 0, maxCh);
                }
                catch
                {
                    return 0;
                }
            }

            throw new NotImplementedException(String.Format(CultureInfo.InvariantCulture, Resources.ERRUnsupportedCalibrationMethod, this.polynomialOrder));
        }

        // Token: 0x06000735 RID: 1845 RVA: 0x00029EF8 File Offset: 0x000280F8
        public override EnergyCalibration Clone()
        {
            return new PolynomialEnergyCalibration(this);
        }

        public override string ToString()
        {
            string result = "";
            string xpow = "";
            string sign = "";
            for(int i = 0; i < this.coefficients.Length; i++)
            {
                if (this.coefficients[i] != 0)
                {
                    if (i == 0)
                    {
                        xpow = "";
                        if (this.coefficients[i] > 0)
                        {
                            sign = "+";
                        }
                        else
                        {
                            sign = "";
                        }
                    }
                    if (i == 1)
                    {
                        xpow = "*x";
                        if (this.coefficients[i] > 0 && i != this.coefficients.Length - 1)
                        {
                            sign = "+";
                        }
                        else
                        {
                            sign = "";
                        }
                    }
                    if (i > 1)
                    {
                        xpow = "*x^" + i.ToString(CultureInfo.InvariantCulture);
                        if (this.coefficients[i] > 0 && i != this.coefficients.Length - 1)
                        {
                            sign = "+";
                        }
                        else
                        {
                            sign = "";
                        }
                    }

                    result = sign + this.coefficients[i].ToString(CultureInfo.InvariantCulture) + xpow + result;
                }
            }
            return "y = " + result;
        }

        public override int MaxChannels()
        {
            return maxChannels;
        }

        // Token: 0x040003A4 RID: 932
        int polynomialOrder;

        // Token: 0x040003A5 RID: 933
        double[] coefficients;

        bool dirty = false;

        int maxChannels = 8192;
        double maxEnergy = -1;

        EnergyToChannelMemo energytochanel = null;
    }
}
