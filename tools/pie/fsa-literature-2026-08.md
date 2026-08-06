# Статьи по FSA и отклику сцинтилляторов — что берём (06.08.2026)

> 🔨 **Нашли проблему — строкой в [`TODO.md`](../../TODO.md) в корне.**

Поиск по литературе под цель «улучшение модели и матрицы отклика» (HPGe — мимо
по условию). Ниже — только то, из чего вытекает действие у нас; полные ссылки
в конце. Строки TODO: F11 (новая), правки F1, P3, V7, новые S9 и E10.

## 1. Непропорциональность светового выхода — главная дыра нашей матрицы

Прибор меряет НЕ поглощённую энергию, а свет, и свет на единицу энергии у
CsI(Tl)/NaI(Tl) зависит от энергии каждого электрона (до ±5 % ниже 60 кэВ у
CsI(Tl) — Mengesha). Наша матрица отклика раскладывает по ПОГЛОЩЁННОЙ энергии —
события с разным составом электронов (один фотоэлектрон против цепочки
комптонов) кладутся в один бин, хотя света дают разное. Последствия — ровно
наши болячки: форма/положение пиков внизу шкалы, «калибровки врут ниже опорных
линий», V2 (модель разрешения ниже 180 кэВ не работает).

Рецепт готовый и дешёвый для нашей архитектуры:
* отклик считать в СВЕТОВОЙ шкале: каждый электронный вклад E_e взвешивать
  кривой относительного выхода L(E_e) (Hayashi 2024 делает это поверх EGS5 и
  получает стабильные <10 % по флюенсу; Breitenmoser 2022 показывает, что без
  непропорциональности матрица высокого разрешения просто неверна);
* фотонный отклик из электронного строится свёрткой по распределению
  электронов (Rooney/Valentine 1997) — у нас распределение электронов УЖЕ
  разыгрывается в `InCrystal`, нужен только множитель L(E) на каждый вклад;
* кривую L(E) для конкретного кристалла можно вывести БЕЗ электронных
  измерений — байесовским фитом по форме комптоновского края наших же
  спектров (Breitenmoser 2023, Nature Comm.);
* стартовые кривые L(E): CsI(Tl)/CsI(Na) — Mengesha 1997, NaI(Tl) —
  Rooney/Valentine.

Это **F11** — кандидат №1: одна ручка объясняет сразу низ шкалы, «горб» ПШПВ
и часть остатка формы пика.

## 2. Каскадное суммирование (F1) — есть путь дешевле розыгрыша схем

* EFFTRAN-подход (Vidmar 2011): CF считается ДЕТЕРМИНИРОВАННО из кривой
  пиковой эффективности, кривой ПОЛНОЙ эффективности и схемы распада — без
  розыгрыша распадов; сходится с полным МК в ~1 %. У нас есть всё: ε_пик
  (симулятор), ε_полн (тот же прогон: 1 − «ничего не задело»), пары совпадений
  с долями (`gamma_coincidence`, 128 429 пар). Первая версия F1 — формулой.
* Décombaz 1992: если делать розыгрышем — считать в ОДНОМ прогоне сразу
  «кажущуюся» и «истинную» эффективность, CF = их отношение (никаких двух
  прогонов).
* Lépy 2023: опубликованный банк эталонных задач по CF — готовая поверка
  нашей реализации (геометрии германиевые, но CF-механика та же).
* Androulakaki 2016: эталонные спектры для FSA генерировались MCNP-CP — то
  есть С КАСКАДНЫМИ СОВПАДЕНИЯМИ ВНУТРИ ОБРАЗА. Наши образы компонентов
  сумм-пиков не содержат — после F1 сумм-пики должны попасть в образы
  (сейчас сумм-пик Cs-134/Co-60 при близкой геометрии ложится в невязку).

## 3. FSA-механика — подтверждения и два заимствования

* Caciolli 2011 (70 цитирований): FSA = NNLS + подстройка энергетической
  калибровки внутри фита — ровно наша связка (NNLS + сетка дрейфа) ✓; плюс
  их калибровка эталонных спектров по полевым спектрам, а не по падам —
  созвучно нашей S4 (`--standard`).
* Xu 2020: **разреженность** (sparsity) как регуляризатор отбора активных
  нуклидов при спектральном разложении — прямой кандидат в замену нашего
  бинарного вето (P3): фантомы давятся штрафом за число компонентов, а не
  поимённым исключением.
* Xu 2022: пороги решения и характеристические пределы (МДА-аналог) для
  пуассоновского спектрального разложения — готовая метрология для FSA
  (наша S9): сейчас у разложения нет ни порога решения, ни предела
  обнаружения.
* Ryu 2026: конвейер «unfold → кандидатный список → байесовская оценка
  активностей» на NaI — архитектурно то же, что наш «поиск пиков задаёт
  состав библиотеки», подтверждение выбора.

## 4. Маринелли и самопоглощение (V7, E6)

* Çetinkaya 2025: FLUKA-факторы самопоглощения для маринелли 0.5/1 л,
  ρ = 0…2.8 г/см³, 238–2614 кэВ, ПЛЮС аналитическая формула фактора от
  (E, ρ). Готовая внешняя сверка нашей маринелли-модели, не требующая
  ни корпуса, ни ЛСРМ (E10). Тот же смысл у Nabil 2023 (полуэмпирическая
  ε(E, ρ)).
* Kim 2026: включение переноса электронов (и фотонов сцинтилляции) в МК
  сокращает расхождение пиковой эффективности с измерением (13.6 % → 7.9 %
  в GEANT4) — довод, что наш V5 (вылет электрона) стоит возвращать не
  прямолинейным CSDA, а переносом.

## Полные ссылки

1. Breitenmoser, «Experimental and Simulated Spectral Gamma-Ray Response of a
   NaI(Tl) Scintillation Detector…» (Adv. Geosci., 2021).
2. Breitenmoser et al., «Numerical Derivation of High-Resolution Detector
   Response Matrices for Airborne Gamma-Ray Spectrometry Systems» (IEEE
   NSS/MIC, 2022).
3. Breitenmoser et al., «Emulator-based Bayesian inference on non-proportional
   scintillation models by Compton-edge probing» (Nature Communications, 2023).
4. Mengesha et al., «Light yield nonproportionality of CsI(Tl), CsI(Na), and
   YAP» (IEEE NSS, 1997).
5. Rooney et al., «Scintillator light yield nonproportionality: calculating
   photon response using measured electron response» (IEEE TNS, 1997).
6. Valentine et al., «The light yield nonproportionality component of
   scintillator energy resolution» (IEEE NSS, 1997).
7. Hayashi et al., «Gamma-Ray Spectroscopy Using an Unfolding Method with
   Response Functions Including the Energy Dependencies of Scintillation
   Efficiency for the NaI(Tl) Scintillator» (J. Radiat. Prot. Res., 2024).
8. Vidmar et al., «Calculation of true coincidence summing corrections for
   extended sources with EFFTRAN» (Appl. Radiat. Isot., 2011).
9. Décombaz et al., «Coincidence-summing corrections for extended sources in
   gamma-ray spectrometry using Monte Carlo simulation» (NIM A, 1992).
10. Lépy et al., «A benchmark for Monte Carlo simulations in gamma-ray
    spectrometry Part II: True coincidence summing correction factors»
    (Appl. Radiat. Isot., 2023).
11. Androulakaki et al., «In situ γ-ray spectrometry in the marine environment
    using full spectrum analysis for natural radionuclides» (Appl. Radiat.
    Isot., 2016).
12. Caciolli et al., «A new FSA approach for in situ γ-ray spectroscopy»
    (Sci. Total Environ., 2011).
13. Xu et al., «Sparse spectral unmixing for activity estimation in γ-ray
    spectrometry applied to environmental measurements» (Appl. Radiat. Isot.,
    2020).
14. Xu et al., «Analysis of gamma-ray spectra with spectral unmixing — Part I:
    Determination of the characteristic limits…» (Appl. Radiat. Isot., 2022).
15. Ryu, «Unfolding-assisted Bayesian quantification of radionuclide mixtures
    in NaI(Tl) spectra with overlapping gamma-ray peaks» (Appl. Radiat.
    Isot., 2026).
16. Çetinkaya, «Determination of self-attenuation correction factors for
    3″×3″ NaI(Tl)… Marinelli beakers» (Radiat. Phys. Chem., 2025).
17. Nabil et al., «A semi-empirical method for efficiency calibration of an
    HPGe detector against different sample densities» (Appl. Radiat. Isot.,
    2023).
18. Kim et al., «A Study on the Simulated Spectra Responses of a Scintillation
    Detector by Tracking Secondary Particle…» (J. Radiat. Prot. Res., 2026).
