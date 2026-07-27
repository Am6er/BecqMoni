# BecqMoni
English:

Compiled distr with autoupdate available at https://amba.cloud/becqmoni

Youtube channel for feature view available at https://www.youtube.com/@Am6er

User config files stored at %AppData%\BecqMoni

Russian:

Дистрибутив с инсталлятором доступен на https://amba.cloud/becqmoni

Youtube канал, где можно посмотреть нововведения https://www.youtube.com/@Am6er

Конфигурационные файлы сохраняются в пользовательском профиле %AppData%\BecqMoni

Using [SpecUtils binding for C#](https://github.com/Am6er/SpecUtilsCS)
For original [SpecUtils](https://github.com/sandialabs/SpecUtils) big thanks SandiaLabs!

## Verification: the spectrum corpus

There is no unit-test project. The peak-detection, deconvolution and library-fit code paths
are verified offline against a corpus of real spectra in `tools/LibraryFitLab/corpus`:
**46 spectra from 18 detector setups**, spanning 0.22 % (HPGe) to 15 % (Obsidian) FWHM at
662 keV, 1024 to 16384 channels and 0.14 M to 251 M counts, with a manifest stating what each
sample contains.

**Every measurement runs over the whole corpus. Numbers obtained on a subset do not go into
the journal and do not support a conclusion.** The earlier results rested on nine spectra from
three similar scintillators inside one third of a resolution decade, and conclusions drawn
there did not survive contact with the rest: a significance gate that helps on one detector
can disable the library fit entirely on another, and only the full corpus makes that visible.

`tools/LibraryFitLab/README.md` is the lab notebook — set composition, phantom lines,
significance gates, and what was tried and thrown away. Read it before touching the
library fit.

## Проверка: корпус спектров

Юнит-тестов в проекте нет. Поиск пиков, деконволюция и библиотечный фит проверяются офлайн
на корпусе реальных спектров в `tools/LibraryFitLab/corpus`: **46 спектров с 18 конфигураций
детекторов**, разрешение от 0.22 % (HPGe) до 15 % (Obsidian) на 662 кэВ, от 1024 до 16384
каналов, от 0.14 М до 251 М отсчётов, с манифестом содержимого каждого образца.

**Любое измерение гоняется по всему корпусу целиком. Числа, полученные на подмножестве, в
журнал не идут и вывода не обосновывают.** Прежние результаты держались на девяти спектрах
трёх похожих сцинтилляторов в пределах трети по разрешению, и выводы, сделанные там, не
пережили встречи с остальными: гейт значимости, помогающий одному детектору, может полностью
выключить библиотечный фит на другом, и видно это только на полном корпусе.

`tools/LibraryFitLab/README.md` — лабораторный журнал: состав сетов, фантомные линии, гейты
значимости, что пробовалось и что выброшено. Читать до того, как трогать библиотечный фит.
