# Проверка математики и физики EfficiencyMaker / FullSpectrumAnalysis — 29.09.2026

Постановка Amber 29.09.2026, дословно:

> Делай проверку на баги. Ищи ошибки в приложении в EfficiencyMaker и FullSpectrumAnalysis и записывай их в TODO. Математика, Физика. Если надо - иди в интернет и ищи статьи для этого.
> Порядок работы - СКИЛЛ РАСПАРАЛЛЕЛИВАНИЯ на много агентов поиска разных багов НЕ ПОДКЛЮЧАЕМ!
> Нашёл баг - проверил его на минимуме (не на всём корпусе) - задокументировал его в TODO с префиксом AMBER. Следи за недельными токенами! Как будут подходить к концу - заканчивай работу. Коммиты и правки в коде - не делать.

Разбор выполнен одним агентом, после чтения навыка аналитика и TODO/DONE.
Изменяются только реестр и этот журнал. Исходники приложения, базы, матрицы,
конфигурации и DONE не меняются. Первоначальные незакоммиченные изменения
трёх строк TODO принадлежат пользователю и сохраняются.

Проверяемый HEAD: `767731d4`. Отдельная сборка текущего дерева:
`BecquerelMonitor/bin/Debug_AmberAudit/BecquerelMonitor.exe`, MSBuild Debug,
`SignManifests=false`, `GenerateManifests=false`; код сборки 0.
Короткие диагностические программы компилируются только в игнорируемый
каталог этой сборки. Проверка не является прогоном или оценкой всего корпуса.
Начальное показание недельного лимита: использовано 0%; промежуточное — 4%.

## AMBER136

**Два интерполятора одной матрицы нарушают площадь каскадного сумм-пика. P2.**

Места:

- `EfficiencyMaker/ResponseMatrix.cs:1938–1942`: амплитуды строк между
  узлами интерполируются линейно по энергии.
- `FullSpectrumAnalysis/FsaCascadeSummer.cs:3596–3631`: эффективности пика
  и полного заноса тех же строк интерполируются логарифмически по обеим осям.
- `FullSpectrumAnalysis/FsaAnalyzer.cs:12983–12993`: чтобы положить сумм-пик
  площадью `Area`, его вес делится на эффективность второго интерполятора,
  затем умножается на строку первого. Результат равен
  `Area * epsilon_matrix / epsilon_summer`, вместо `Area`.
- Те же эффективности участвуют в выживании линий, площадях пар и сумм-континууме
  (`FsaCascadeSummer.cs:1840–1854, 2998–3012`, `FsaAnalyzer.cs:13085–13104`).

Минимальное измерение на двух существующих матрицах **known**, без анализа
спектров и без пересчёта матриц: вызвать `AccumulateChannel(..., Peak)`,
просуммировать строку и сравнить с `FsaCascadeSummer.PeakEfficiency(E)`.
Для суммы всех каналов — `Evaluate(E).Sum()` против `TotalEfficiency(E)`.
Числа характеризуют только эти входы, не весь корпус.

| Матрица | E, кэВ | Пик из строки | Пик суммирователя | Отношение − 1 |
|---|---:|---:|---:|---:|
| AS80_point0 | 17.14 | 0.000752810571991776 | 0.000723500613604230 | +4.0511% |
| AS80_point0 | 26.34 | 0.0407184274452430 | 0.0406675459803302 | +0.1251% |
| AS80_point0 | 661.657 | 0.0612258969925971 | 0.0612130028762470 | +0.0211% |
| RC103_marinelli05_kcl | 17.14 | 0.000011796716834052 | 0.000011605161531957 | +1.6506% |
| RC103_marinelli05_kcl | 1332.492 | 0.000075085947652179 | 0.000075014860660528 | +0.0948% |

Это самосогласованность двух читателей одного файла; шум Монте-Карло здесь
не объясняет разность. В узлах правила совпадают; между узлами — нет.
Дополнительный изолированный контроль **самого `AccumulateSumPeaks`**:
искусственная матрица с узлами 100/300 кэВ, только пики площадью 0.4/0.1,
шаг бина 1 кэВ. Заданная площадь сумм-пика 0.04:

```text
E=100  actual=0.040000000000000000
E=200  actual=0.059951157228030343  (+49.87789%)
E=300  actual=0.040000000000000000
```

Вызван текущий метод приложения через reflection, без изменения его тела.
Крупная разность относится к специально редкой двухузловой матрице,
а не к приведённым выше реальным матрицам. Контроль в узлах положительный.
Не утверждается, что сумм-пик реального нуклида на 17.14 кэВ присутствует:
это контроль нормировки на заданной энергии. Цена на реальных активностях
не измерена. Устранение должно согласовать знаменатель нормировки с реально
накладываемой строкой и сохранить её заданную площадь.

## AMBER137

**Закреплённая на верхней границе амплитуда считается свободной при вычислении
ошибок остальных амплитуд. P2.**

Правка ограничения наложений из закрытой `AMBER123` реализована в
`FsaAnalyzer.ApplyAmplitudeCaps`. Она правильно фиксирует значение на пределе
и заново решает оставшуюся задачу, но затем выставляет
`reducedActive[capped] = cap > 0` (`FsaAnalyzer.cs:11500–11502`).
`FitOnce` строит `ActiveIndices` из этих флагов и обращает исходную матрицу
Грама вместе с закреплённой колонкой (`:11744–11786`). Тот же список уменьшает
`ndf`. Погрешность описывает другую задачу — где предел снят.

Трёхканальная проба вызывает **сам `FitOnce` текущей сборки через reflection**:

```text
signal = [1, 0, 0]
pileup = [0.9, 0.1, 0]
y      = [10, 1, 0]
weights= [1, 1, 1]
```

| Предел pileup | Амплитуды signal,pileup | sigma(signal) | z(signal) | ndf |
|---|---|---:|---:|---:|
| нет, контроль | 1, 10 | 9.0553851381 | 0.1104315261 | 1 |
| 0, контроль | 10, 0 | 1 | 10 | 2 |
| 1, дефект | 9.1, 1 | 9.0553851381 | 1.0049268873 | 1 |

При закреплённом `pileup=1` остаётся функция
`Q(a)=(10-a-0.9)^2+(1-0.1)^2`. Минимум `a=9.1`, кривизна по `a` даёт
`sigma=1`, свободен один параметр, `ndf=2`. Остаток `chi2=0.81`, поэтому
множитель `sqrt(max(1,chi2/ndf))` не маскирует пример. Код даёт ошибку
в 9.055 раза больше. При нулевом пределе всё сходится: значение 0 исключено
из активного множества. Это положительный контроль причины.

Путь до прикладных решений: `ColumnSigma` (`:8574–8578`) возвращает
`fit.Sigma[k]` у активного нуклида; значимости используются при отборе состава
и проверке членов ряда. Возможен необоснованный отказ от слабого нуклида
рядом с колонкой наложений, достигшей предела. Влияние на конкретный реальный
спектр пока не измерено. Следует различать положительную амплитуду в модели
и свободный параметр в статистике, включая пределы и отчётный `ndf`.

## AMBER138

**Повтор нижней энергии проходит защиту интерполятора, TryEval возвращает
успех с NaN. P3.**

`FsaEfficiency` удаляет дубли сравнением исходной энергии с
`Math.Exp(logEnergy[last])` (`FsaEfficiency.cs:44`). Однако в .NET Framework
`Exp(Log(1000.0)) = 999.9999999999998`. Повтор `1000` проходит проверку,
логарифмы соседних узлов равны, знаменатель интерполяции равен нулю.

Проба использует публичный `FromConfig` и `TryEval`:

```text
Curve = [(1000, 0.1), (1000, 0.2)]
FromConfig != null: True
TryEval(1000, out efficiency, out error): True
efficiency: NaN
```

Кривой с одной различной энергией быть не должно: собственный контракт
`FromPoints` требует два годных узла. Это также относится к повтору нижнего
узла в более длинной кривой. У других энергий знак округления иной, поэтому
одни повторы отбрасываются, другие сохраняются.

Читатели: `BecquerelCoefficient.ForLine` и путь зоны ROI, нормировка спектра
в `SpectrumAriphmetics`. `ForLine` дополнительно проверяет `eps > 0` и
отказывает как `NoEpsilon`; **утверждения, что NaN уже попадает в отображаемые
беккерели, нет**. Подтверждён дефект валидации и контракта общего интерполятора.
Для корректной кривой с уникальными энергиями этот пример неприменим.
Исправление: сравнение исходных энергий либо их логарифмов между собой,
проверка числа различных узлов; текущая задача разрешает только запись.

## AMBER139

**Распределение тормозных фотонов берётся от нижнего узла, а число фотонов —
от текущей энергии. Непрерывная энергия электрона даёт ступенчатый спектр. P2.**

Места: `EfficiencyMaker/BremsstrahlungData.cs:396–424` (`SampleFrom`),
`:789–843` (`IndexBelow`, `Interpolate`). `SampleKev` и `SampleStepKev`
берут одну строку `table[IndexBelow(teKev)]` без интерполяции по `teKev`.
`Photons`, `Radiated`, `StepPhotons`, `StepRadiatedPerGram` интерполируют
скалярные моменты между узлами. Поэтому число фотонов и их энергия принадлежат
разным распределениям. Само название `Radiated` не доказывает сохранение энергии
в реальном розыгрыше.

Читатели обеих ветвей в приложении: `ElectronTransport.cs:1463, 1485, 1543,
1565`, `EfficiencySimulator.cs:5526, 7591`, а также запасное тормозное слоёв
`ElectronTransport.cs:1134`. Умолчания включают перенос с тормозным по пути.

Минимум: один материал CsI, состав по атомным массам текущей `matdb`,
`ElectronData.ByName("CsI")`, `ThickTargetBrem.For(material,electron,5)`.
Таблица имеет 96 узлов от 5 до 3000 кэВ. Никаких геометрий или спектров.
Среднее публичных `SampleKev`/`SampleStepKev` — детерминированная квадратура
по 100000 равномерным квантилям `u=(i+0.5)/100000`, не случайная выборка.
Сравнение толстой мишени: `mean(SampleKev)*Photons/Radiated - 1`;
тонкого шага: `mean(SampleStepKev)*StepPhotons(T,1,1)/StepRadiatedPerGram - 1`.

| T, кэВ | Среднее SampleKev, кэВ | Radiated/Photons, кэВ | Ошибка момента, толстая | Ошибка момента, тонкая |
|---:|---:|---:|---:|---:|
| 100 | 19.57443378 | 19.89885187 | −1.6303% | −1.7099% |
| 300 | 34.88205912 | 35.85989030 | −2.7268% | −2.8911% |
| 662 | 54.28390524 | 55.44487419 | −2.0939% | −2.2868% |
| 1000 | 68.96827848 | 70.92206312 | −2.7548% | −3.1304% |
| 2614 | 131.58570329 | 137.67532936 | −4.4232% | −5.1692% |

Положительный контроль причины — пройти через границу узла:

```text
T=103.49905435091389  Photons=0.034581174359095843  mean=19.574433779074969
T=103.49905642089500  Photons=0.034581175322188677  mean=20.286179464615770
```

Энергия электрона изменилась на 0.00000207 кэВ, число фотонов непрерывно,
средняя энергия фотона скачком выросла на 3.64%. На тонком шаге среднее
26.37629542 → 27.43357079 кэВ. Квадратура использует те же квантили на обоих
плечах, поэтому шумом это объяснить нельзя. Кроме выбора нижней строки,
согласовать следует и внутрибиновую форму: CDF обращается линейно по log(k),
тогда как моменты в `Build`/`BuildThin` накоплены трапециями по k.

Физический критерий: энергетический момент того же дифференциального спектра
задаёт излучённую энергию. Разделение дискретного излучения по порогу и его
интегралы приведены в [Geant4 Physics Reference Manual, Livermore
bremsstrahlung](https://geant4.web.cern.ch/documentation/pipelines/master/prm_html/PhysicsReferenceManual/electromagnetic/electron_incident/bremsstrahlung/livermore_bremsstrahlung.html).
Внешний источник не заменяет приведённую самопроверку реализации.
Это **не** закрытый остаток `M10` ниже 1 кэВ и не прежняя невязка `M7` с ESTAR:
сейчас расходятся генератор и собственный момент одной готовой таблицы выше
5 кэВ. Цена на эффективности детектора не измерена.

## AMBER140

**У нижней отсечки таблица тормозного возвращает отрицательное число фотонов
и отрицательную излучённую энергию. P3.**

`IndexBelow` возвращает индекс 1 при любом `T <= node[1]`
(`BremsstrahlungData.cs:793–795`), а `Interpolate` ограничивает снизу только
`T <= node[0]` (`:824–826`). Между первым и вторым узлами берутся узлы 1 и 2;
коэффициент `f` отрицателен. При `values[1]=0`, `values[2]>0` ответ отрицателен.

Тот же минимальный материал CsI и публичные методы, без геометрии:

```text
MinKev=5
T=5.1 keV
Photons=-8.9126265775027176e-6
Radiated/Photons=5.1743275609519195 keV
```

Таким образом `Radiated` тоже отрицателен. При T=5.5 кэВ `Photons` уже
положителен: `5.2451985902000137e-6`, то есть это конкретный выход за нижний
интервал, а не отсутствие таблицы. `StepRadiatedPerGram` также уходит ниже нуля;
`StepPhotons` зажимает неположительный результат в 0.

Прикладной путь: `ElectronLoss`/`RestBremsstrahlung` принимают T > 5 кэВ,
после чего `Poisson` получает отрицательное среднее и не рождает фотоны.
**Отрицательное число фотонов в гистограмму не записывается** — дефект таблицы
проявляется как потеря ненулевого излучения над отсечкой и нарушение контракта.
Величина мала; приоритет P3. Исправление нижней границы отдельно от согласования
распределений `AMBER139`: использовать настоящий интервал 0–1 и физически
неотрицательную схему у порога.


## Контроли, воспроизводимость и завершение

Контроль AMBER138: две точки на 100 кэВ отвергаются (`FromConfig == null`),
на 1000 кэВ принимаются. Это зависимость от округления `exp(log(E))`.

Контроль AMBER139: среднее того же генератора при 2614 кэВ вычислено также
аналитически по его кускам CDF. На интервале [k0,k1] условное распределение
равномерно по log(k), поэтому вклад в среднее равен
`(CDF0-CDF1)*(k1-k0)/log(k1/k0)`. Получено 131.58571752854 кэВ (толстая)
и 222.86765304421 кэВ (тонкая). Квадратура отличается на 0.00001424 и
0.00000087 кэВ соответственно: проценты недобора не являются ошибкой квадратуры.

Прямые отрицательные величины AMBER140 на T=5.1 кэВ:
`Radiated = -4.6116849340444889e-5` кэВ,
`StepRadiatedPerGram = -0.95716611644303662` кэВ/(г/см²).

`tools/check_registry.py` завершился кодом 1 с ровно пятью находками:
каждая новая строка ссылается на новый журнал, ещё не добавленный в Git.
В новых строках нет столкновений номеров, отсутствующих файлов или имён кода.
Исторические предупреждения о старых ссылках не исправлялись.
Исключений в сторож не добавлялось, коммита и индексирования файлов нет.
Это ожидаемое ограничение проверки при прямом запрете Amber на коммиты.
Все пять подтверждённых дефектов имеют строки TODO; новых открытых строк +5,
что является прямым результатом постановки «только найти и записать».
Механическая сверка покрытия — 5/5, `git diff --check` — код 0.
Завершение по лимиту: недельный использован на 15%, пятичасовой на 96%.
Дальнейший поиск в этом заходе остановлен из-за близкого пятичасового предела.

Ниже сохранены исходные тексты только диагностических программ, чтобы
воспроизведение не зависело от существования игнорируемого каталога сборки.
Программы вызывают текущие методы приложения; формулы приложения не переписаны
в пробах. Reflection открывает закрытые входы, не подменяя их реализации.

Собрать приложение указанной в начале Debug-командой. Каждый следующий блок
сохранить под его именем в `BecquerelMonitor/bin/Debug_AmberAudit`, затем
компилировать компилятором .NET Framework `Framework64/v4.0.30319/csc.exe`
с `/target:exe`, `/r:BecquerelMonitor.exe`, `/r:System.Core.dll`,
`/r:System.Windows.Forms.dll`, `/r:System.Drawing.dll` из этого каталога.
Рядом с каждым exe скопировать `BecquerelMonitor.exe.config` под именем
`<имя пробы>.exe.config`: необходимы штатные перенаправления сборок SQLite.
Первую пробу запускать из корня репозитория с двумя аргументами:
`tools/CORPUS/corpus/geometries/RC103_marinelli05_kcl.rmx` и
`tools/CORPUS/corpus/geometries/AS80_point0.rmx`; остальные — без аргументов.

### AmberAuditProbe.cs

```csharp
using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
public static class AmberAuditProbe {
 static object Get(object o,string n) { return o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(o); }
 public static void Main(string[] args) {
  System.Threading.Thread.CurrentThread.CurrentCulture=CultureInfo.InvariantCulture;
  var cfg=new EfficiencyConfigData();
  cfg.Curve=new List<ROIEfficiencyData>{new ROIEfficiencyData{Energy=1000,Efficiency=.1},new ROIEfficiencyData{Energy=1000,Efficiency=.2}};
  var eff=FsaEfficiency.FromConfig(cfg); double ep,err;
  Console.WriteLine("duplicate1000 curve={0} valid={1} efficiency={2}",eff!=null,eff.TryEval(1000,out ep,out err),ep);
  foreach(double cap in new[]{double.NaN,0.0,1.0}) {
   var analyzer=new FsaAnalyzer();
   var a=new FsaComponent("signal",FsaComponentKind.Single){FixedTemplate=new[]{1.0,0.0,0.0}};
   var b=new FsaComponent("pileup",FsaComponentKind.Nuisance){FixedTemplate=new[]{.9,.1,0.0},AmplitudeCap=cap};
   var result=typeof(FsaAnalyzer).GetMethod("FitOnce",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(analyzer,new object[]{new List<FsaComponent>{a,b},new List<double[]>(),null,null,null,1.0,0.0,0,2,3,new[]{10.0,1.0,0.0},new[]{1.0,1.0,1.0},null});
   Console.WriteLine("cap={0} amplitude={1} sigma={2} z={3} ndf={4}",cap,string.Join(",",(double[])Get(result,"Amplitude")),string.Join(",",(double[])Get(result,"Sigma")),string.Join(",",(double[])Get(result,"Z")),Get(result,"Ndf"));
  }
  foreach(string path in args) {
   var matrix=ResponseMatrix.Load(path); if(matrix==null)throw new Exception("matrix refused "+path);
   matrix.TransferByChannel=true;
   var summer=FsaCascadeSummer.Create(matrix);
   Console.WriteLine("matrix={0} nodes={1}",System.IO.Path.GetFileName(path),matrix.Energies.Length);
   foreach(double e in new[]{17.14,26.34,31.0,32.0,35.0,59.54,80.9979,121.78,661.657,1173.228,1332.492}) {
    var row=new double[ResponseMatrix.ImageBins(e,matrix.BinKev)+4]; matrix.AccumulateChannel(row,e,1.0,0);
    var total=matrix.Evaluate(e,row.Length);
    Console.WriteLine("E={0} peakMatrix={1:R} peakSummer={2:R} rel={3:F4}% totalMatrix={4:R} totalSummer={5:R}",e,row.Sum(),summer.PeakEfficiency(e),100*(row.Sum()/summer.PeakEfficiency(e)-1),total.Sum(),summer.TotalEfficiency(e));
   }
  }
 }
}
```

### AmberBremAudit.cs

```csharp
using System;
using System.Reflection;
using System.Globalization;
using BecquerelMonitor.EfficiencyMaker;
public static class AmberBremAudit {
 public static void Main() {
  System.Threading.Thread.CurrentThread.CurrentCulture=CultureInfo.InvariantCulture;
  var m=new GeometryMaterial{Name="CsI",Density=4.51};
  double total=MaterialDatabase.AtomicMass[55]+MaterialDatabase.AtomicMass[53];
  m.Fractions[55]=MaterialDatabase.AtomicMass[55]/total;m.Fractions[53]=MaterialDatabase.AtomicMass[53]/total;
  var b=ThickTargetBrem.For(m,ElectronData.ByName("CsI"),5);
  var flags=BindingFlags.NonPublic|BindingFlags.Instance;
  var nodes=(double[])typeof(ThickTargetBrem).GetField("node",flags).GetValue(b);
  Console.WriteLine("nodes={0} range={1:R}..{2:R}",nodes.Length,nodes[0],nodes[nodes.Length-1]);
  foreach(double e in new[]{5.1,5.5,10,100,300,662,1000,2614}) {
   double thick=0,thin=0;int n=100000;
   for(int i=0;i<n;i++){double u=(i+.5)/n;thick+=b.SampleKev(e,u)/n;thin+=b.SampleStepKev(e,u)/n;}
   Console.WriteLine("E={0} N={1:R} thickMean={2:R} thickTarget={3:R} thickRel={4:F4}% thinMean={5:R} thinTarget={6:R} thinRel={7:F4}%",e,b.Photons(e),thick,b.Radiated(e)/b.Photons(e),100*(thick*b.Photons(e)/b.Radiated(e)-1),thin,b.StepRadiatedPerGram(e)/b.StepPhotons(e,1,1),100*(thin*b.StepPhotons(e,1,1)/b.StepRadiatedPerGram(e)-1));
  }
  foreach(double node in nodes) if(node>90 && node<140) {
   foreach(double e in new[]{node*(1-1e-8),node*(1+1e-8)}) {
    double thick=0,thin=0;int n=100000;
    for(int i=0;i<n;i++){double u=(i+.5)/n;thick+=b.SampleKev(e,u)/n;thin+=b.SampleStepKev(e,u)/n;}
    Console.WriteLine("jump E={0:R} N={1:R} thickMean={2:R} thinMean={3:R}",e,b.Photons(e),thick,thin);
   }
  }
 }
}
```

### AmberAuditControl.cs

```csharp
using System;
using System.Linq;
using System.Reflection;
using System.Globalization;
using System.Collections.Generic;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
public static class AmberAuditControl {
 public static void Main() {
  System.Threading.Thread.CurrentThread.CurrentCulture=CultureInfo.InvariantCulture;
  var flags=BindingFlags.NonPublic|BindingFlags.Instance;
  var matrix=new ResponseMatrix{BinKev=1,Energies=new[]{100.0,300.0},TransferByChannel=true};
  matrix.ChannelRows=new float[EfficiencySimulator.ResponseChannelCount][][];
  for(int c=0;c<matrix.ChannelRows.Length;c++){matrix.ChannelRows[c]=new[]{new float[101],new float[301]};}
  matrix.ChannelRows[0][0][100]=.4f;matrix.ChannelRows[0][1][300]=.1f;matrix.RebuildTotals();
  var summer=FsaCascadeSummer.Create(matrix);
  var analyzer=new FsaAnalyzer{ResponseMatrix=matrix};
  typeof(FsaAnalyzer).GetField("cascade",flags).SetValue(analyzer,summer);
  foreach(double e in new[]{100.0,200.0,300.0}) {
   var correction=new FsaCascadeSummer.Correction{SumPeaks=new List<FsaCascadeSummer.SumPeak>{new FsaCascadeSummer.SumPeak(e,.04)}};
   var deposit=new double[601];
   typeof(FsaAnalyzer).GetMethod("AccumulateSumPeaks",flags).Invoke(analyzer,new object[]{deposit,null,correction,false});
   Console.WriteLine("sum E={0} requested=0.04 actual={1:R} relative={2:F5}%",e,deposit.Sum(),100*(deposit.Sum()/.04-1));
  }
  foreach(double e in new[]{100.0,1000.0}) {
   var cfg=new EfficiencyConfigData{Curve=new List<ROIEfficiencyData>{new ROIEfficiencyData{Energy=e,Efficiency=.1},new ROIEfficiencyData{Energy=e,Efficiency=.2}}};
   var curve=FsaEfficiency.FromConfig(cfg); Console.WriteLine("duplicate E={0} accepted={1}",e,curve!=null);
  }
  var mat=new GeometryMaterial{Name="CsI",Density=4.51};double am=MaterialDatabase.AtomicMass[55]+MaterialDatabase.AtomicMass[53];
  mat.Fractions[55]=MaterialDatabase.AtomicMass[55]/am;mat.Fractions[53]=MaterialDatabase.AtomicMass[53]/am;
  var b=ThickTargetBrem.For(mat,ElectronData.ByName("CsI"),5);
  Console.WriteLine("lower T=5.1 photons={0:R} radiated={1:R} stepRadiated={2:R}",b.Photons(5.1),b.Radiated(5.1),b.StepRadiatedPerGram(5.1));
  var nodes=(double[])typeof(ThickTargetBrem).GetField("node",flags).GetValue(b);
  foreach(bool thin in new[]{false,true}) {
   var table=(double[][])typeof(ThickTargetBrem).GetField(thin?"thinAbove":"cumulative",flags).GetValue(b);
   double t=2614;int j=0;while(j+1<nodes.Length&&nodes[j+1]<=t)j++;
   double mean=0;for(int i=0;i<j;i++)mean+=(table[j][i]-table[j][i+1])*(nodes[i+1]-nodes[i])/Math.Log(nodes[i+1]/nodes[i]);
   Console.WriteLine("exact thin={0} mean={1:R}",thin,mean);
  }
 }
}
```

