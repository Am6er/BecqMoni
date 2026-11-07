# Полоса `fixGdmlA`, 05.11.2026: читатель GDML без подмен (`AMBER303`, читательская часть `AMBER292`)

Срез правки: коммит `939102f7` (`AMBER278`), HEAD полосы `e62b2f69`. Отчёт полосы — черновик до вердикта критика.
Правило постановки `AMBER278`: незнакомое — без подмены. Расчёт из импорта закрыт и остаётся закрытым; правка делает честными сводку и будущий расчёт.

Файлы: `BecquerelMonitor/EfficiencyMaker/GdmlGeometry.cs`, `GdmlG4Materials.cs`, новая проба `tools/effmaker/probes/GdmlReaderProbeA303.cs`, входы `tools/effmaker/probes/data/gdml_a303/` (184 файла, около 600 КБ на диске, все синтетические, или взяты у критиков и из `gen.py`; цепочки и `e20000` проба пишет сама и снимает). Первая и вторая сдачи `GdmlSketch.cs` не трогали; третий заход дописал в нём `tube`, `cone`, `union` (`DrawUnion`, подпись `GdmlSketchUnion`); контур объединения переделан позже, полосой `designGdml`. Формы не тронуты, окна не выводились, `BecquerelMonitor.exe` не запускался. `Resources*` эта полоса не правила, см. раздел «Ключи».

## Как проверить

```powershell
# сборка: только через PowerShell, свои каталоги
MSBuild BecquerelMonitor\BecquerelMonitor.csproj /t:Build /p:Configuration=Debug /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\fixGdmlA\' /p:IntermediateOutputPath='obj\fixGdmlA\'
pwsh tools\effmaker\probes\build_all.ps1 -Bin BecquerelMonitor\bin\fixGdmlA -Out tools\effmaker\probes\build_fixGdmlA
# проба: код 0 — сошлось, 1 — расхождение
tools\effmaker\probes\build_fixGdmlA\GdmlReaderProbeA303.exe --tmp=D:\BqMoni_Claude\fixGdmlA --g4=<G4NistMaterialBuilder.cc> --real=<8 настоящих файлов>
```

Итог на момент третьего захода: **новый читатель — 199 входов, расхождений 0, код 0; старый (`e62b2f69`) — та же проба, расхождений 158, код 1.** С входами `x03`, `u14`, `u25`, `a_hex_*` (Н-Б, Н-В критика `critGdml5`, полоса `designGdml`) — 205 входов, расхождений 0.

Опасные входы проба гоняет дочерним процессом со сторожем памяти 1500 МБ и выключенным окном отчёта Windows о падении.

## Третий заход: `critGdml4` со статьями и настоящим Geant4; `tube`, `cone`, `union` (`AMBER312`)

Критик `critGdml4` собрал настоящий читатель Geant4 11.4.2 (`D:\BqMoni_Claude\critGdml4\g4gdml\`) и сравнил: на 8 настоящих файлах 341 том сошёлся. Не принят один пункт `AMBER303`, находки Н1–Н10. Затем решение Amber 05.11.2026 вопросником, дословно: **«Добавить tube, union, cone (Рекомендую)»**: строка `AMBER312`. Связка пересобрана: `D:\BqMoni_Claude\fixGdmlA\g4gdml\dump.exe`, сверка `D:\BqMoni_Claude\fixGdmlA\cmp_run.ps1 -G4`, вывод `cmp_real.txt`, `cmp_ex.txt`, `cmp_ex2.txt`, `cmp_own.txt`.

| находка | было | стало | вход |
|---|---|---|---|
| Н1 | ложное «Geant4 refuses» на `<constant name="TWOPI">`, потом теряются повороты; строка в 107 из 144 файлов | значения и занятые имена разделены: `Taken` — 192 имени `setStdMath` и `setSystemOfUnits`, файлу определять нельзя; `PI`, `TWOPI`, `HALFPI` не заняты, значение файла главнее. Не определены в файле: Geant4 отказывает, здесь берётся обычное значение и строка `GdmlWarnConstantAssumed` | `o10a_const_twopi` (полиякон 0.785398), по 144 файлам критика ложных строк было 107, стало 0 |
| Н2 | `<!ENTITY>`: текст про `DtdProcessing` | запрет DTD остался, текст человеческий (`GdmlFatalDtd`) | `a_entity`, `G01/entity.gdml` |
| Н3 | сетка наизнанку: `Math.Abs` | `six < 0` — строка и «не измерено» (`GdmlReasonMeshInverted`) | `o05_tess_inverted` |
| Н4 | `G01/axes.gdml`: «не замкнута» | перекос чётный на каждом ребре — «часть граней вывернута» (`GdmlReasonMeshFlipped`) | `e02_flipped_face`, `axes.gdml`; контроль `30_tess_open` |
| Н5 | корень `gdml_simple_extension` — отказ | читается как `gdml` со строкой, неизвестные разделы — строка | `a_root_extension`, `G03/color_extension.gdml` (было 0, стало 1 том) |
| Н6 | `detLV0x8f9550` в показе | `StripName` как `G4GDMLRead::StripName`: вещества, элементы, изотопы и ссылки на них хранятся без хвоста (так у Geant4), тома и тела — только в показе | `a_hex_names`, `G02/test.gdml` |
| Н7 | «solid : unsupported body <loop>», «copies are not counted ()» | `GdmlWarnLoop` с границами цикла; `opticalsurface` — не тело, своя строка | `a_loop_opt`, `G01/loop.gdml`, `opticalsurfaces.gdml` |
| Н8 | габарит неполного оборота как у полного | перенос `G4GeomTools::DiskExtent` случай за случаем | `o02_partial_phi`, `a_tube_phi` |
| Н9 | ESR 0.333 | доли делятся на сумму, как `G4Material::FillVectors` (`G4Material.cc:566`); проверено по исходнику и дампу. Масса та же | `a_mat_sum`, `ESR_x_log` в `--real` |
| Н10 | номера строк в комментариях; `scale` 1.000037 терял положение | номера по 11.4.2. `scale`: как `G4ReflectionFactory::Place`: без отражения величина отбрасывается, с отражением единицы обязательны с точностью 1e-8 (иначе Geant4 отказывает файлу и положение неизвестно). `define/scale` + `scaleref` читаются | `a_scale_near1`, `a_scale_ref`, `a_scale_half_mirror` |

Новые тела:

* `tube`, `cone` хранятся как полиякон из двух плоскостей (`Kind = Polycone`, `Tag` исходный), поэтому объём, масса, габарит и чертёж общие. Правила G4Tubs и G4Cons: z ≤ 0, Δφ ≤ 0 или пропущенный — отказ со строкой, у трубы ещё rmin ≥ rmax.
* `union` (`GdmlSolidKind.Union`): `first`, `second`, положения и повороты, вложенные. Объём точен, когда габариты не встречаются (так же `G4UnionSolid`), когда тело лежит внутри осевой коробки другого и когда оба — осевые коробки. Иначе Geant4 разыгрывает точки, а здесь «не измерено» с причиной: на `a_union_overlap` у Geant4 8.266 см³ оценкой.
* Чертёж (`GdmlSketch.cs`): `DrawUnion`, заливка общей областью, контур каждой части вне другой, подпись `GdmlSketchUnion`, тот же бюджет `MaxViewVertices` и предел глубины. Площади на `GdmlSketchProbeA304`: кольцо, сектор, трапеция, 15×10.

Сверка с настоящим Geant4 (`cmp.py`):

* 8 настоящих файлов: 341 том; `dv 0`, `dpos 0`, `dbb 0`, `dcomp 0`, `drho 0`. `unk_v 18` — незамкнутые сетки (3 тела в 6 файлах; Geant4 и сам предупреждает «holes in the surface»). `dm 2`: `RC103_device_log` 0 г против 1.0e-7 г, `RC110_device_log` 0 г против 5.8e-7 г; `DetectorModule_log` RC103 у Geant4 −7e-5 г, здесь `?`.
* Свои входы с новыми телами: `a_tube*`, `a_cone*`, `a_union_*`, сходятся с дампом, кроме `a_union_overlap` (см. выше).
* Зеркало: дамп строит габарит отражённого тома неверно (`a_scale_ref`, `o07`: z 0…50 при отражении). Проверено настоящей навигацией `dump.exe --locate`: точка (0, 0, −25) в `A_refl`, (0, 0, 25) в мире. Здесь −50…0, как и надо.

Примеры `extended/persistency/gdml`, 22 файла. Было — снимок критика `critGdml4/res_ex`, до этого захода:

| пример | томов у Geant4 | томов было | с объёмом было | томов стало | с объёмом стало |
|---|---|---|---|---|---|
| `assembly.gdml` | 2 | 0 | 0 | 0 | 0 |
| `auxiliary.gdml` | 1 | 1 | 1 | 1 | 1 |
| `axes.gdml` | 7 | 7 | 4 | 7 | 4 |
| `block.gdml` | 4 | 0 | 0 | 0 | 0 |
| `color_extension.gdml` | 1 | 0 | 0 | 1 | 1 |
| `divisionvol.gdml` | 2 | 1 | 1 | 1 | 1 |
| `entity.gdml` | 1 | 0 | 0 | 0 | 0 |
| `loop.gdml` | 125 | 0 | 0 | 0 | 0 |
| `mat_nist.gdml` | 1 | 1 | 1 | 1 | 1 |
| `matricesandloops.gdml` | 9 | 1 | 0 | 1 | 0 |
| `multiUnion.gdml` | 1 | 1 | 0 | 1 | 0 |
| `opticalsurfaces.gdml` | 2 | 2 | 2 | 2 | 2 |
| `pTube.gdml` | 3 | 2 | 2 | 2 | 2 |
| `parameterized.gdml` | 3 | 2 | 2 | 2 | 2 |
| `replicated.gdml` | 2 | 1 | 1 | 1 | 1 |
| `scale.gdml` | 20 | 20 | 4 | 20 | 4 |
| `scaledSolids.gdml` | 16 | 16 | 0 | 16 | 0 |
| `scaledTube.gdml` | 20 | 20 | 2 | 20 | 2 |
| `solids.gdml` | 26 | 26 | 4 | 26 | 6 |
| `tess.gdml` | 2 | 2 | 2 | 2 | 2 |
| `test.gdml` | 24 | 23 | 17 | 23 | 23 |
| `G04/auxiliary.gdml` | 1 | 1 | 1 | 1 | 1 |
| **итого** | **273** | **127** | **44** | **128** | **53** |

Остальное различие с Geant4: непрочитанные `assembly`, `loop`, `paramvol`, `replicavol`, `divisionvol`, `matrix`, `scaledSolid`, `multiUnion` и тела вне постановки — каждое строкой сводки. Найдено попутно, в постановке нет: `G4_*` вне встроенной таблицы (`o11_nist_outside`: `G4_BGO`, `G4_TEFLON`, `G4_Ge`, `G4_Be`) — неизвестный состав со строкой, хотя у Geant4 они есть.

Сведение: `names_notdrawn_20000` теперь рисуется (трубы поддержаны): 4.7 с на 20000 тел, как у 20000 полияконов; предела числа простых тел в чертеже нет и раньше не было.

## Второй заход: вердикт критика `critFixGdmlA`

Первая сдача не принята. Группы 3, 5, 7 и пункты П2, П3, П4, П6–П12 сошлись, ниже — что поправлено по находкам. Входы — в пробе, группа «критик» и `Hazard`.

| находка | было | стало | вход |
|---|---|---|---|
| Н1 блокер, регрессия первой сдачи | `element.Value` у `<expression>` рекурсивен: 20000 вложенных `<a>` — 0xC00000FD на главном потоке (старый читатель код 0) | `TryDirectText`: только прямые текстовые узлы, вложенный элемент — строка и константа не заводится. Других рекурсивных обходов XML в чтении нет: `.Value` у `XElement` был один, `Descendants` и `ToString` у `XElement` не зовутся | `e20000` |
| Н2 блокер | `1/(1/0)` и `1/(1e308*10)` давали 0 молча | деление на 0 и неконечный промежуточный результат — отказ со строкой, как `Evaluator.cc:328` | `p03_zero_div`, `a_div_overflow`, `n10` |
| Н3 | вещество, тело, том дважды: читался последний; элемент и изотоп тоже | **первое со строкой**: `G4Material::GetMaterial`, `G4Element::GetElement`, `G4Isotope::GetIsotope`, `G4SolidStore`, `G4LogicalVolumeStore` при `reverseSearch` по умолчанию. Значение по умолчанию `false` взято из памяти о `G4GDMLRead.hh`: этого заголовка в `g4\` нет | `t1_material_twice`, `a_element_twice`, `a_isotope_twice`, `a_solid_twice`, `a_volume_twice`, `s05_vol_twice` |
| Н3 | `position`, `rotation` дважды: молчали | последнее, как `positionMap[name]`, со строкой | `t2_position_twice`, `a_rotation_twice` |
| Н3 | константа дважды или с именем единицы: последнее молча | Geant4 падает (`G4GDMLEvaluator::DefineConstant`). Два разных значения выбрать не из чего: строка и всё, что на неё ссылается, — «не измерено». Одно значение дважды — читается, со строкой | `d07_const_redefined`, `d09_shadow_unit`, `a_const_same` |
| Н4 | `composite n="0"` — пустой состав без следа, шапка 0 | строка и неизвестный состав в шапке; то же при `fraction n="0"` | `t06`, `t05` |
| Н5 | самопересекающийся `genericPolycone`: V=1.047 | `ProfileCrossesItself` как `G4ReduciblePolygon::CrossesItself(1/kInfinity)`, не измерено. Профиль длиннее 10000 точек не проверяется и потому не измеряется, со строкой | `y15_gp_self`, контроль `y14_gp_cw` |
| Н6 | рёбра сравнивались точным `==` | `MergeVertices`: вершина берёт номер первой, что ближе 0.5e-9 мм, как `CreateVertexList`. 168, 168, 104 и 38 замкнутых тел без изменений | `e04_dup_vertex_1e-12`, `e1_relative_nondyadic`, контроль `a_dup_vertex_1e-8` (остаётся незамкнутой) |
| Н7 | `deltaphi` 6.28319 рад, 0, −1, 720°: отказ, текст округлялся до «= 360 deg is outside» | полный оборот со строкой и исходным текстом значения, как `G4Polycone.cc:229`. Пропущенный обязательный: Geant4 читает 0, то есть полный оборот; так же и здесь, со строкой | `yf1`, `yf5`, `yf6`, `yf7`, `yf3`, `a_deltaphi_bad`, `a_deltaphi_missing` |
| Н8 | после 500 строк причина предела томов отбрасывалась, а журнал обещал строку | `WarnPriority`: предел томов и шагов, отсутствие мира и томов, сборка и запасной мир не отбрасываются никогда | `k1_dag_after_500_warnings` |
| Н9 | `k2`: «more than 100000 volumes (stopped at 1)»; `w05`: «1 cm3 of its 1 cm3»; `w02`: ребёнок снаружи назван перекрытием; двойной пробел при пустой ссылке; пустое имя вершины | своя строка про шаги; числа длиннее, когда совпали при печати; «перекрывают его или выступают наружу»; `no <solidref>`, «пустая ссылка», «N face(s) skipped» без имени | `k2_steps_limit`, `w05`, `w02`, `v01`, `s1`, `e07` |
| Н11 | `s05`: том дважды и запасной мир; `type="relative"` читался как RELATIVE; нет имени, нет `materialref` молчали | первое из `volumeOrder`; регистр значим, прочее ABSOLUTE как у Geant4, со строкой; строки про имя и ссылку | `s05`, `e05`, `s2`, `t14`, `v01`, `v02` |
| Несколько `setup` без Default | первая молча | `GetSetup`: единственная сборка — как бы ни называлась, из нескольких Default; без неё первая со строкой | `s02` |

Сверх вердикта, просьба полосы `fixGdmlB`:

* `VolumeLine`: единицы через `GdmlUnitMm`, `GdmlUnitCm3`, `GdmlUnitG`. Порядок строки тома изменён: имя, размеры, объём, масса, потом состав в скобках. Состав длинный, и при переносе по словам числа уходили вниз; проба сверяет порядок на `32_container`.
* `DetectAxis`: образ локальной z у самого большого тела вращения, а не «z при любом повороте». `34_pc_rot_end` и `03_rot_sign` дают x, настоящие файлы GS2020 — z (проверка в `--real`). Тома с непрочитанным положением в выбор не входят.

Оставлено сознательно:

* `composite ref="G4_H"`: Geant4 падает (там нужен элемент), здесь верное число по единственному элементу вещества. Находка Н11, решение распорядителя: оставить и записать.
* Н10: разбор секции на 300000 вложенных уровней квадратичен (`XElement.Load`). Не регрессия, файл искусственный; предел глубины XML не ставился. На `e20000` — 1.1 с.

## По пунктам

Номера строк «было» — `e62b2f69`, «стало» — рабочее дерево полосы.

| п. | было (`файл:строка`) | стало | контроль |
|---|---|---|---|
| 1 | `:1977` NaN и ∞ проходили, `1e400` → 0 молча; рекурсия без предела `:1121-1159`, `:1299`, `:984-1041`; `:1439` цикл и взрыв | `TryNumber :3907` отказывает неконечному, каждый читатель числа даёт строку; `MaxSolidDepth = 256` (`:2348`), `MaxMaterialDepth = 32` (`:2053`); `AddInstance :3049`: стек пути (`:3063`), предел `MaxInstances = 100000` и шагов обхода (`:3059`), при превышении структура не строится | `16a`, `16b`, `16d`, `16e`, `c9`, `c1`–`c3`, `c10`, `23_cycle2`, `d1_dag`, цепочки 200000 |
| 2 | `:1473` масса = V·ρ | `SetMass :3165`, `MeasureChildren :3214`: (V − ΣV прямых детей)·ρ; неизвестный объём ребёнка, `replicavol`, потерянный ребёнок — масса `?` и причина; отрицательный остаток — строка о перекрытии и `?` | `32_container`, `a_overlap`, `a_child_*`, настоящие файлы |
| 3 | `:153`, `:962` матрица без обращения; `:321`, `:1948`, `:2000` угол без единицы как градусы | `FromPhysvol :200` = транспонированная `FromEuler :171`; вычитание берёт `FromEuler`, как `G4GDMLReadSolids.cc:185`; умолчание угла — радиан (`AngleDefaultScale`), длины — мм; атрибуты единиц у `<gdml>` не читаются и дают строку | `03`: x[−50;0]; (30,40,50): образ +z (−0.6428, 0.3830, 0.6634); `05`; `06`: V = 3.92699 |
| 4 | `:854-873`, `:905-960` незнакомое молча | `ReadStructure`, `ReadPhysvol :1864`: всё незнакомое, кроме `auxiliary`, — строка; `replicavol`, `paramvol`, `divisionvol`, `loop` названы и закрывают массу матери; `physvol/file` с именем файла; `scale` ±1 применяется зеркалом и называется, прочее — положение неизвестно | `08`, `09`, `10`, `a_tags_copies`, `a_tag_unknown` |
| 5 | `:480`, `:615-617`, `:641-643`, `:673-674`, `:928-934`, `:952-958`, `:1932-1934`, `:1950-1952` | пропущенные `x`,`y`,`z` у `position` и `rotation` и `rmin` у `zplane` = 0 (`Component :3883`), прочие обязательные — отказ телу; встроенные `position`, `rotation` читаются; вычислитель `GdmlExpression :3938`: `+-*/()`, константы файла, `pi`, `TWOPI`, `HALFPI`, единицы, глубина скобок 64; функции и степень — строка-отказ; `quantity` с единицей `:731`; `lunit` на грани `ReadTessellated :1581` | `04a`–`04c`, `07a`–`07f`, `a_expr_refused`, `31`, `a_face_lunit`, `a_tess_relative`, `c9` |
| 6 | `:392`, `:1411-1425` | `ReadSetup :620`, `FindWorld :3004`: мир из `setup/world`; нет такого — строка; «World» и первый без ссылок — запасные, со строкой | `11`, `a_setup_missing` |
| 7 | `:695-710`, `:1337-1356`, `:596-597`, `:1539` | `ReadSubtraction :1501` читает `position(ref)`, `rotation(ref)`, `firstposition(ref)`, `firstrotation(ref)`; `IntersectionVolume :2865`: только первое тело-коробка; не пересекает → 0, внутри → V₂, осевая коробка → точно, иначе причина; второго нет — объём не известен (`FindSolid`); шапка `:3306` считает вычитание от незнакомого | `12`: V=1; `13`, `13b`: не измерено; `c7`; `22_union`: неподдержанных 3; `a_sub_*` |
| 8 | `:2022`, `:2039`, `:2056` незнакомое = 1 | `TryLengthScale :4173`, `TryAngleScale :4199`, `TryDensityScale :4218`: `km`, `Ang`, `fm` и длинные имена; прочее — строка и «не измерено»; регистр значим, как в Geant4 | `15a`–`15c`, `a_unit_*`, `a_angle_unit_bad` |
| 9 | `:494-500`, `:539-546`, `:1026-1030`, `:1042`, `:1534` | `ReadMaterial :852`, `ReadElement :992`, `ReadIsotope :1092`, `ResolveEntries :2162`, `ResolveEntry :2237`: `Z`+`atom`; `element`; изотопы; `composite` атомами → массовые доли; вещество с одной плотностью — неизвестный состав и в смеси, и в шапке; ссылка в составе — как `MixtureRead`: элемент, потом вещество | `14a`–`14e`, `a_mat_*` |
| 10 | `GdmlG4Materials.cs:38,63-64` | сталь 8.00, Fe 0.7462, Cr 0.1690, Ni 0.0848; `G4_S` 2.0; `G4_AIR` с C 0.000124; шапка переписана | `--g4`: 34 строки сошлись, наибольшая \|Σ−1\| = 1e-6 |
| 11 | `:1286-1287`, `:512`, `:533`, `:1139-1140`, `:1205` | `ComputeTessellated :2551`, `CountUnpairedEdges :2639`: непарные рёбра по координатам → не измерено; плотность ≤0 или NaN — `?` со строкой; доля NaN — состав неполон; `rmin > rmax`, отрицательный размер — строка | `30`, `a_tess_closed`, `c4`, `c5`, `c11`–`c13` |
| 12 | — | `GdmlReaderProbeA303.cs` | этот журнал |

Атомные массы: единственная таблица приложения `AttenuationData.AtomicMass` (`MaterialDatabase`, `xcom_elements` базы `matdb.sqlite`), второй таблицы не заведено, запись в `*.sqlite` нет. Масса `atom` из файла приоритетнее. Загружается база только когда `composite` без `atom` реально встретился.

## Что нашлось сверх постановки

* **Таблица G4: посылка строки уже реальности.** Построчная сверка нашла ещё две строки: `G4_Al` 2.6989 → **2.699**, `G4_N` 0.00116528 → **0.0011652**. Исправлены: `G4_Al` входит во все настоящие файлы, поэтому массы алюминия выросли в 1.000037 раза.
* **`Z` вместе с долями.** В `RC103_detector` и `RC110_detector` записано `<material Z="55" name="G4_CESIUM_IODIDE">` с долями Cs и I. Первая версия пункта 9 читала это как чистый Cs, и контроль шапки (0 неизвестных составов) этого не видел; поймало сравнение «было — стало» настоящих файлов. По `MaterialRead` при составляющих `Z` не значит ничего. Теперь так же; вход `a_mat_z_and_fraction` и проверка `Crystal_log` в `--real`.
* **`Math.Cos` за 2^63 возвращает аргумент.** Вход `c2b` (`x="1e300" unit="deg"`) давал матрицу с элементами 1e299 и габарит 1e299 при кубе в 10 мм. Теперь угол за 1e9 градусов — строка и положение неизвестно (`:3841`).
* **Две ловушки скорости.** Код `long` — XOR половин, у ключа ребра (a, b) он вырождает словарь: чтение GS2020 шло 1050 мс при 350 на старом; `EdgeComparer` вернул 300–356. Предел составов 256 давал 35 с на цепочке 200000 (каждая сборка мусора обходит глубокий стек), при 32 — 1.9 с. Настоящие составы — 2–3 уровня. Строка причины вложенного вычитания на каждом уровне росла и съедала память на цепочке (сторож 1500 МБ) — теперь передаётся как есть.

## Положительный контроль

Мутанты третьего захода (`D:\BqMoni_Claude\fixGdmlA\mut.txt`), мини-сборка читателя, та же проба:

== контроль: код 0, отказов 0: 

== TWOPI занято: код 1, отказов 2: третий o10a_const_twopi; третий a_tube_rad_cm

== сетка наизнанку по модулю: код 1, отказов 3: 1 c1b_1e39_vertex; 1 c1c_1e300_vertex; третий o05_tess_inverted

== корень gdml обязателен: код 1, отказов 1: третий a_root_extension

== хвост имени не снят: код 1, отказов 1: третий a_hex_names

== габарит сектора как круг: код 1, отказов 3: третий o02_partial_phi; третий a_tube_phi; третий a_cone_hole_phi

== scale как величина: код 1, отказов 1: третий a_scale_near1

== union: сумма без пересечения: код 1, отказов 1: третий a_union_boxes

== union: второе тело без положения: код 1, отказов 6: третий a_union_disjoint; третий a_union_boxes; третий a_union_overlap

== tube: z как половина: код 1, отказов 8: 7 22_union; третий a_tube; третий a_tube_rad_cm

== доли без нормирования: код 1, отказов 2: 9 a_mat_sum; настоящие RC103_detector

Первая и вторая сдачи — на старом читателе:

Старый читатель собран из исходников `e62b2f69` (`git show`, sha256 `5a19d1d1…` `GdmlGeometry.cs`, `97e1f4ad…` `GdmlG4Materials.cs`) в отдельную сборку `BecquerelMonitor.dll` с заглушкой ресурсов, рядом положен тот же `GdmlReaderProbeA303.exe`; проба читает только поля, которые были и до правки. Каталог `D:\BqMoni_Claude\fixGdmlA\old\`.

Опасные входы на старом: `23_cycle2` и `d1_dag` — сторож памяти (1502 и 1534 МБ, код 0x4D), цепочки 200000 `subtraction` и `material` — **0xC00000FD**. На новом: код 0; 41 мс и 24 МБ, 616 мс и 75 МБ, 1.5 с и 340 МБ, 1.9 с и 345 МБ.

Строк таблицы 193. «было OK» — проверка не отказала и на старом, изменилось только число (плотность `G4_Al`) или добавилась строка `setup`.

| группа | вход | было | стало |
|---|---|---|---|
| 1 | `16a_nan` | A: V=? m=? x[NaN;NaN] z[NaN;NaN] {Al 1.0000}; строк=0 | A: V=? m=? габарит ? {Al 1.0000}; строк=2 «box P: x = "NaN" is not a finite number or a known expression» |
| 1 | `16b_inf` | A: V=Infinity m=Infinity x[-Infinity;Infinity] z[NaN;NaN] {Al 1.0000}; строк=0 | A: V=? m=? габарит ? {Al 1.0000}; строк=2 «box P: x = "Infinity" is not a finite number or a known expression» |
| 1 | `16d_nan_pc` | A: V=? m=? x[NaN;NaN] z[NaN;NaN] {Al 1.0000}; строк=0 | A: V=? m=? габарит ? {Al 1.0000}; строк=3 «polycone P: rmax = "NaN" is not a finite number or a known expression» |
| 1 | `16e_pos_inf` | A: V=3.926991 m=10.59856 x[1.797693E+308;Infinity] z[NaN;NaN] {Al 1.0000}; строк=0 | A: V=3.926991 m=10.59895 габарит ? {Al 1.0000}; строк=2 «physvol pv_A: x = "Infinity" is not a finite number or a known expression» |
| 1 | `c9_1e400` | A: V=? m=? x[0;0] z[-5;5] {Al 1.0000}; строк=0 | A: V=? m=? габарит ? {Al 1.0000}; строк=1 «box P: x = "1e400" is not a finite number or a known expression» |
| 1 | `c1_nan_vertex` | A: V=? m=? x[NaN;NaN] z[NaN;NaN] {Al 1.0000}; строк=0 | A: V=? m=? x[0;100] z[0;100] {Al 1.0000}; строк=2 «define/position a: x = "NaN" is not a finite number or a known expression» |
| 1 | `c2_rot_nan` | A: V=3.926991 m=10.59856 x[NaN;NaN] z[NaN;NaN] {Al 1.0000}; строк=0 | A: V=3.926991 m=10.59895 габарит ? {Al 1.0000}; строк=1 «physvol pv_A: x = "NaN" is not a finite number or a known expression» |
| 1 | `c3_gp_nan` | A: V=? m=? x[NaN;NaN] z[NaN;NaN] {Al 1.0000}; строк=0 | A: V=? m=? габарит ? {Al 1.0000}; строк=2 «genericPolycone P: r = "NaN" is not a finite number or a known expression» |
| 1 | `c10_rmax_1e300` | A: V=Infinity m=Infinity x[-1E+300;1E+300] z[0;50] {Al 1.0000}; строк=0 | A: V=? m=? x[-1E+300;1E+300] z[0;50] {Al 1.0000}; строк=1 «polycone P: volume is out of range» |
| 1 | `16c_huge (было OK)` | A: V=1E+299 m=2.6989E+299 x[-5E+299;5E+299] z[-5;5] {Al 1.0000}; строк=0 | A: V=1E+299 m=2.699E+299 x[-5E+299;5E+299] z[-5;5] {Al 1.0000}; строк=1 |
| 1 | `c1b_1e39_vertex` | A: V=1.666667E+39 m=4.498167E+39 x[0;1E+39] z[0;100] {Al 1.0000}; строк=0 | A: V=? m=? x[0;1E+39] z[0;100] {Al 1.0000}; строк=1 «tessellated P: the faces are turned inside out (negative volume); volume is not defined» |
| 1 | `c1c_1e300_vertex` | A: V=1.666667E+300 m=4.498167E+300 x[0;1E+300] z[0;100] {Al 1.0000}; строк=0 | A: V=? m=? x[0;1E+300] z[0;100] {Al 1.0000}; строк=1 «tessellated P: the faces are turned inside out (negative volume); volume is not defined» |
| 1 | `c2b_rot_big` | A: V=3.926991 m=10.59856 x[-5;5] z[-8.726646E+298;9.599311E+299] {Al 1.0000}; строк=0 | A: V=3.926991 m=10.59895 габарит ? {Al 1.0000}; строк=1 «physvol pv_A: angle 1E+300 0 0 deg is out of range» |
| 1 | `24_cycle1` | A: V=3.926991 m=0.004731199 x[-5;5] z[0;50] {N 0.7553, O 0.2318, Ar 0.0128}; A: V=3.926991 m=0.004731199 x[-5;5] z[0;50] {N 0.7553, O 0.2318, Ar 0.012… | A: V=3.926991 m=? x[-5;5] z[0;50] {C 0.0001, N 0.7553, O 0.2318, Ar 0.0128}; строк=2 «structure: volume A contains itself (placement pv_A); the copy i… |
| 1 | `25_subcycle (было OK)` | A: V=? m=? габарит ? {Al 1.0000}; строк=1 «solid X: reference cycle» | A: V=? m=? габарит ? {Al 1.0000}; строк=2 «solid X: reference cycle» |
| 1 | `26_matcycle (было OK)` | A: V=3.926991 m=? x[-5;5] z[0;50]; неподд=0 сост=1; строк=1 «material M1: reference cycle» | A: V=3.926991 m=? x[-5;5] z[0;50]; неподд=0 сост=1; строк=2 «material M1: reference cycle» |
| 1 | `23_cycle2` | код 0x0000004D memory guard: 1519 MB | код 0x00000000 volumes=1 warnings=3 ms=82 peakMB=25 «structure: volume A contains itself (placement pv_A); the copy is not placed» |
| 1 | `d1_dag` | код 0x0000004D memory guard: 1509 MB | код 0x00000000 volumes=0 warnings=40 ms=1049 peakMB=82 «structure: the file places more than 100000 volumes (stopped at 100000); the structure is not … |
| 1 | `k1_dag_after_500_warnings` | код 0x0000004D memory guard: 1507 MB | код 0x00000000 volumes=0 warnings=502 ms=909 peakMB=85 «structure: the file places more than 100000 volumes (stopped at 100000); the structure is not … |
| 1 | `sub_chain_200000` | код 0xC00000FD | код 0x00000000 volumes=1 warnings=501 ms=2527 peakMB=342 «solid S256: bodies are nested deeper than 256 levels; volume is not measured» |
| 1 | `mat_chain_200000` | код 0xC00000FD | код 0x00000000 volumes=1 warnings=501 ms=3590 peakMB=341 «material M32: references are nested deeper than 32 levels; composition is not read» |
| 1 | `e20000` | код 0x00000000 volumes=1 warnings=1 ms=3221 peakMB=32 | код 0x00000000 volumes=1 warnings=1 ms=1857 peakMB=32 «define/expression E: nested elements are not an expression» |
| 1 | `k2_steps_limit` | код 0x0000004D memory guard: 1504 MB | код 0x00000000 volumes=0 warnings=3 ms=12388 peakMB=665 «structure: walking the placements took more than 1000000 steps (1 volumes placed); the struct… |
| 2 | `32_container` | Housing: V=1000 m=2698.9 x[-50;50] z[-50;50] {Al 1.0000}; Cry: V=729 m=2673.243 x[-45;45] z[-45;45] {Na 0.1534, I 0.8466}; строк=0 | Housing: V=1000 m=731.429 x[-50;50] z[-50;50] {Al 1.0000}; Cry: V=729 m=2673.243 x[-45;45] z[-45;45] {Na 0.1534, I 0.8466}; строк=1 |
| 2 | `a_overlap` | A: V=1 m=2.6989 x[-5;5] z[-5;5] {Al 1.0000}; Inner: V=8 m=62.992 x[-10;10] z[-10;10] {Fe 1.0000}; строк=0 | A: V=1 m=? x[-5;5] z[-5;5] {Al 1.0000}; Inner: V=8 m=62.992 x[-10;10] z[-10;10] {Fe 1.0000}; строк=1 «volume A: children take 8 cm3 of its 1 cm3: in t… |
| 2 | `a_child_unknown` | A: V=1 m=2.6989 x[-5;5] z[-5;5] {Al 1.0000}; Inner: V=? m=? габарит ? {Fe 1.0000}; неподд=1 сост=0; строк=1 «solid Tb: unsupported body <sphere>» | A: V=1 m=? x[-5;5] z[-5;5] {Al 1.0000}; Inner: V=? m=? габарит ? {Fe 1.0000}; неподд=1 сост=0; строк=1 «solid Tb: unsupported body <sphere>» |
| 2 | `a_child_missing` | A: V=1 m=2.6989 x[-5;5] z[-5;5] {Al 1.0000}; строк=1 «volume A: child Ghost not found» | A: V=1 m=? x[-5;5] z[-5;5] {Al 1.0000}; строк=1 «volume A: child Ghost not found» |
| 3 | `03_rot_sign` | A: V=3.926991 m=10.59856 x[-3.061516E-16;50] z[-5;5] {Al 1.0000}; строк=0 | A: V=3.926991 m=10.59895 x[-50;3.061516E-16] z[-5;5] {Al 1.0000}; строк=1 |
| 3 | `34_pc_rot_end` | A: V=7.853982 m=21.19711 x[-50;50] z[-5;5] {Al 1.0000}; строк=0 | A: V=7.853982 m=21.1979 x[-50;50] z[-5;5] {Al 1.0000}; строк=1 |
| 3 | `33_pc_offset_y (было OK)` | A: V=282.7433 m=763.096 x[-30;30] z[-50;50] {Al 1.0000}; строк=0 | A: V=282.7433 m=763.1243 x[-30;30] z[-50;50] {Al 1.0000}; строк=1 |
| 3 | `28_ylong_box (было OK)` | A: V=20 m=53.978 x[-10;10] z[-5;5] {Al 1.0000}; строк=0 | A: V=20 m=53.98 x[-10;10] z[-5;5] {Al 1.0000}; строк=1 |
| 3 | `05_rot_default_rad` | A: V=3.926991 m=10.59856 x[-4.998121;6.368728] z[-0.1370607;50.11827] {Al 1.0000}; строк=0 | A: V=3.926991 m=10.59895 x[-50;1.339745E-07] z[-5;5.000001] {Al 1.0000}; строк=1 |
| 3 | `06_polycone_no_aunit` | A: V=0.06853892 m=0.1849797 x[-5;5] z[0;50] {Al 1.0000}; строк=1 «polycone P: partial revolution 6.28319 deg» | A: V=3.926991 m=10.59895 x[-5;5] z[0;50] {Al 1.0000}; строк=1 |
| 3 | `a_rot_304050` | A: V=3.926991 m=10.59856 x[-4.746149;41.7883] z[-5.129049;38.29975] {Al 1.0000}; строк=0 | A: V=3.926991 m=10.59895 x[-37.53552;5.39614] z[-4.229418;37.40011] {Al 1.0000}; строк=0 |
| 3 | `a_angle_unit_bad` | A: V=3.926991 m=10.59856 x[-5;5] z[0;50] {Al 1.0000}; строк=0 | A: V=3.926991 m=10.59895 габарит ? {Al 1.0000}; строк=1 «physvol pv_A: unknown unit "DEG"» |
| 3 | `a_root_unit` | A: V=3.926991 m=10.59856 x[-5;5] z[0;50] {Al 1.0000}; строк=0 | A: V=3.926991 m=10.59895 x[-5;5] z[0;50] {Al 1.0000}; строк=2 «<gdml>: attribute lunit="cm" is ignored, as in Geant4; the defaults stay mm and rad» |
| 4 | `08_replica` | A: V=1000 m=1.20479 x[-50;50] z[-50;50] {N 0.7553, O 0.2318, Ar 0.0128}; строк=0 | A: V=1000 m=? x[-50;50] z[-50;50] {C 0.0001, N 0.7553, O 0.2318, Ar 0.0128}; строк=2 «volume A: <replicavol> is not supported, its copies are not coun… |
| 4 | `09_scale` | A: V=3.926991 m=10.59856 x[-5;5] z[0;50] {Al 1.0000}; строк=0 | A: V=3.926991 m=10.59895 x[-5;5] z[-50;0] {Al 1.0000}; строк=2 «physvol pv_A: <scale> 1 1 -1 is read as a mirror» |
| 4 | `10_file` | A: V=3.926991 m=10.59856 x[-5;5] z[0;50] {Al 1.0000}; строк=1 «volume World: child  not found» | A: V=3.926991 m=10.59895 x[-5;5] z[0;50] {Al 1.0000}; строк=2 «physvol fromfile: external file sub.gdml is not read; volume World is incomplete» |
| 4 | `a_tags_copies` | A: V=1000 m=2698.9 x[-50;50] z[-50;50] {Al 1.0000}; строк=0 | A: V=1000 m=? x[-50;50] z[-50;50] {Al 1.0000}; строк=3 «volume A: <paramvol> is not supported, its copies are not counted (4 x A)» |
| 4 | `a_tag_unknown` | A: V=3.926991 m=10.59856 x[-5;5] z[0;50] {Al 1.0000}; строк=0 | A: V=3.926991 m=10.59895 x[-5;5] z[0;50] {Al 1.0000}; строк=2 «volume A: unsupported <frobnicate>» |
| 5 | `04a_pos_inline_partial` | A: V=3.926991 m=10.59856 x[-5;5] z[0;50] {Al 1.0000}; строк=0 | A: V=3.926991 m=10.59895 x[-5;5] z[30;80] {Al 1.0000}; строк=1 |
| 5 | `04b_rot_inline_partial` | A: V=3.926991 m=10.59856 x[-5;5] z[0;50] {Al 1.0000}; строк=0 | A: V=3.926991 m=10.59895 x[-50;3.061516E-16] z[-5;5] {Al 1.0000}; строк=1 |
| 5 | `04c_pos_define_partial` | A: V=3.926991 m=10.59856 x[-5;5] z[0;50] {Al 1.0000}; строк=2 «define/position p: coordinates are not numbers» | A: V=3.926991 m=10.59895 x[-5;5] z[30;80] {Al 1.0000}; строк=1 |
| 5 | `07a_expr_rmin` | A: V=62.83185 m=169.5769 x[-20;20] z[0;50] {Al 1.0000}; строк=0 | A: V=47.12389 m=127.1874 x[-20;20] z[0;50] {Al 1.0000}; строк=1 |
| 5 | `07b_expr_pos` | A: V=3.926991 m=10.59856 x[-5;5] z[0;50] {Al 1.0000}; строк=0 | A: V=3.926991 m=10.59895 x[-5;5] z[6;56] {Al 1.0000}; строк=1 |
| 5 | `07c_expr_box` | A: V=? m=? x[0;0] z[-5;5] {Al 1.0000}; строк=0 | A: V=1 m=2.699 x[-5;5] z[-5;5] {Al 1.0000}; строк=1 |
| 5 | `07d_quantity_unit` | A: V=0.5 m=1.34945 x[-2.5;2.5] z[-5;5] {Al 1.0000}; строк=0 | A: V=5 m=13.495 x[-25;25] z[-5;5] {Al 1.0000}; строк=1 |
| 5 | `07e_const_expr` | A: V=62.83185 m=169.5769 x[-20;20] z[0;50] {Al 1.0000}; строк=0 | A: V=47.12389 m=127.1874 x[-20;20] z[0;50] {Al 1.0000}; строк=1 |
| 5 | `07f_twopi` | A: V=? m=? x[-5;5] z[0;50] {Al 1.0000}; строк=1 «polycone P: partial revolution 0 deg» | A: V=3.926991 m=10.59895 x[-5;5] z[0;50] {Al 1.0000}; строк=2 «TWOPI is not defined in the file: its usual value is taken, Geant4 would refuse the fil… |
| 5 | `a_expr_refused` | A: V=? m=? x[0;0] z[-5;5] {Al 1.0000}; строк=0 | A: V=? m=? габарит ? {Al 1.0000}; строк=1 «box P: x = "sqrt(2)" is not a finite number or a known expression» |
| 5 | `31_tess_lunit` | A: V=0.0001666667 m=0.0004498167 x[0;1] z[0;1] {Al 1.0000}; строк=0 | A: V=0.0001666667 m=0.0004498333 x[0;1] z[0;1] {Al 1.0000}; строк=2 «tessellated P: lunit="cm" on the body is ignored, as in Geant4; only a face carri… |
| 5 | `a_face_lunit` | A: V=0.0001666667 m=0.0004498167 x[0;1] z[0;1] {Al 1.0000}; строк=0 | A: V=0.1666667 m=0.4498333 x[0;10] z[0;10] {Al 1.0000}; строк=0 |
| 5 | `a_tess_relative` | A: V=? m=? x[0;100] z[0;100] {Al 1.0000}; строк=1 «tessellated P: 2 face(s) skipped» | A: V=166.6667 m=449.8333 x[0;100] z[0;100] {Al 1.0000}; строк=0 |
| 6 | `11_setup_world` | томов 0; строк=0 | Det: V=3.926991 m=44.57135 x[-5;5] z[0;50] {Pb 1.0000}; строк=0 |
| 6 | `a_setup_missing` | A: V=3.926991 m=10.59856 x[-5;5] z[0;50] {Al 1.0000}; строк=0 | A: V=3.926991 m=10.59895 x[-5;5] z[0;50] {Al 1.0000}; строк=2 «setup: world volume Nope not found» |
| 6 | `01_box_cm` | A: V=6000 m=16193.4 x[-50;50] z[-150;150] {Al 1.0000}; строк=0 | A: V=6000 m=16194 x[-50;50] z[-150;150] {Al 1.0000}; строк=1 |
| 7 | `12_sub_second_pos` | A: V=0 m=0 x[-5;5] z[-5;5] {Al 1.0000}; строк=1 «subtraction Sub: unsupported <positionref>» | A: V=1 m=2.699 x[-5;5] z[-5;5] {Al 1.0000}; строк=1 |
| 7 | `13_sub_inside_hole` | A: V=30.63053 m=82.66873 x[-30;30] z[-10;10] {Al 1.0000}; строк=0 | A: V=? m=? x[-30;30] z[-10;10] {Al 1.0000}; строк=2 «subtraction Sub: overlap with Core is not computed: first body Ring is <polycone>, only a …» |
| 7 | `13b_sub_nested` | A: V=750 m=2024.175 x[-50;50] z[-50;50] {Al 1.0000}; строк=0 | A: V=? m=? x[-50;50] z[-50;50] {Al 1.0000}; строк=2 «subtraction S2: overlap with B1 is not computed: first body S1 is <subtraction>, only a bo…» |
| 7 | `36_ctrl_sub (было OK)` | A: V=0.936 m=2.52617 x[-5;5] z[-5;5] {Al 1.0000}; строк=0 | A: V=0.936 m=2.526264 x[-5;5] z[-5;5] {Al 1.0000}; строк=1 |
| 7 | `a_sub_box_partial` | A: V=0 m=0 x[-5;5] z[-5;5] {Al 1.0000}; строк=1 «subtraction S: unsupported <position>» | A: V=0.5 m=1.3495 x[-5;5] z[-5;5] {Al 1.0000}; строк=0 |
| 7 | `a_sub_rot_partial` | A: V=0 m=0 x[-5;5] z[-5;5] {Al 1.0000}; строк=2 «subtraction S: unsupported <position>» | A: V=? m=? x[-5;5] z[-5;5] {Al 1.0000}; строк=1 «subtraction S: overlap with B2 is not computed: it only partly overlaps the box and is not…» |
| 7 | `a_sub_inside (было OK)` | A: V=999.2146 m=2696.78 x[-50;50] z[-50;50] {Al 1.0000}; строк=0 | A: V=999.2146 m=2696.88 x[-50;50] z[-50;50] {Al 1.0000}; строк=0 |
| 7 | `c7_sub_missing` | A: V=1 m=2.6989 x[-5;5] z[-5;5] {Al 1.0000}; строк=1 «solid S: reference NOPE not found» | A: V=? m=? x[-5;5] z[-5;5] {Al 1.0000}; строк=1 «subtraction S: reference NOPE not found» |
| 7 | `22_union` | A: V=? m=? габарит ? {Al 1.0000}; C: V=? m=? габарит ? {Al 1.0000};  ...+1неподд=2 сост=0; строк=2 «solid Tb: unsupported body <tube>» | A: V=1 m=2.699 x[-5;5] z[-5;5] {Al 1.0000}; C: V=? m=? x[-5;5] z[-5;5] {Al 1.0000};  ...+1строк=2 «subtraction SS: overlap with B1 is not computed: fi… |
| 7 | `a_unknown_sphere` | A: V=? m=? x[-25;25] z[-25;25] {Al 1.0000}; строк=1 «solid P: unsupported body <sphere>» | A: V=? m=? x[-25;25] z[-25;25] {Al 1.0000}; неподд=1 сост=0; строк=1 «solid P: unsupported body <sphere>» |
| 8 | `15a_unit_km` | A: V=0.001 m=0.0026989 x[-0.5;0.5] z[-0.5;0.5] {Al 1.0000}; строк=0 | A: V=1E+15 m=2.699E+15 x[-500000;500000] z[-500000;500000] {Al 1.0000}; строк=1 |
| 8 | `15b_unit_upper` | A: V=1 m=2.6989 x[-5;5] z[-5;5] {Al 1.0000}; строк=0 | A: V=? m=? габарит ? {Al 1.0000}; строк=2 «box P: unknown unit "CM"» |
| 8 | `15c_density_bad` | A: V=3.926991 m=3.926991 x[-5;5] z[0;50] {Fe 1.0000}; строк=0 | A: V=3.926991 m=? x[-5;5] z[0;50] {Fe 1.0000}; строк=2 «material M: unknown unit "kg/cm3"» |
| 8 | `a_unit_ang_fm` | A: V=1E+21 m=2.6989E+21 x[-5E+07;5E+07] z[-5E+07;5E+07] {Al 1.0000}; строк=0 | A: V=1 m=2.699 x[-5;5] z[-5;5] {Al 1.0000}; строк=0 |
| 8 | `a_unit_fm` | A: V=1E+33 m=2.6989E+33 x[-5E+11;5E+11] z[-5E+11;5E+11] {Al 1.0000}; строк=0 | A: V=0.001 m=0.002699 x[-0.5;0.5] z[-0.5;0.5] {Al 1.0000}; строк=0 |
| 9 | `14a_mat_Z` | A: V=3.926991 m=44.57135 x[-5;5] z[0;50]; строк=2 «material Lead: unsupported <atom>» | A: V=3.926991 m=44.57135 x[-5;5] z[0;50] {Pb 1.0000}; строк=1 |
| 9 | `14b_mat_composite` | A: V=3.926991 m=3.926991 x[-5;5] z[0;50] {H 2.0000, O 1.0000}; строк=2 «material H2O: <composite> read as a mass fraction (atom fractions are not conv… | A: V=3.926991 m=3.926991 x[-5;5] z[0;50] {H 0.1119, O 0.8881}; строк=1 |
| 9 | `14c_mat_element` | A: V=3.926991 m=3.926991 x[-5;5] z[0;50]; неподд=0 сост=1; строк=4 «materials: unsupported <element>» | A: V=3.926991 m=3.926991 x[-5;5] z[0;50] {H 0.1120, O 0.8880}; строк=1 |
| 9 | `14d_fraction_density_only` | A: V=3.926991 m=11.78097 x[-5;5] z[0;50] {Fe 0.5000}; строк=1 «material DO: density only, composition is unknown» | A: V=3.926991 m=11.78097 x[-5;5] z[0;50] {Fe 0.5000}; неподд=0 сост=1; строк=2 «material DO: density only, composition is unknown» |
| 9 | `14e_steel` | A: V=3.926991 m=31.49447 x[-5;5] z[0;50] {Si 0.0100, Cr 0.1700, Mn 0.0100, Fe 0.7460, Ni 0.1200}; строк=0 | A: V=3.926991 m=31.41593 x[-5;5] z[0;50] {Cr 0.1690, Fe 0.7462, Ni 0.0848}; строк=1 |
| 9 | `a_mat_isotope` | A: V=3.926991 m=39.26991 x[-5;5] z[0;50]; неподд=0 сост=1; строк=8 «materials: unsupported <isotope>» | A: V=3.926991 m=39.26991 x[-5;5] z[0;50] {O 0.1188, U 0.8812}; строк=0 |
| 9 | `a_mat_symbol` | A: V=3.926991 m=3.926991 x[-5;5] z[0;50]; неподд=0 сост=1; строк=2 «material Fe: not found in the file or in the built-in G4 table» | A: V=3.926991 m=3.926991 x[-5;5] z[0;50] {Fe 0.5000, Cu 0.5000}; строк=0 |
| 9 | `a_mat_sum` | A: V=3.926991 m=3.926991 x[-5;5] z[0;50] {Fe 0.7000, Cu 0.7000}; строк=0 | A: V=3.926991 m=3.926991 x[-5;5] z[0;50] {Fe 0.5000, Cu 0.5000}; строк=1 «material M: fractions sum to 1.4, not to 1» |
| 9 | `a_mat_composite_bad` | A: V=3.926991 m=3.926991 x[-5;5] z[0;50] {H 0.1119, O 0.8881}; строк=1 «material M: <composite> read as a mass fraction (atom fractions are not conver… | A: V=3.926991 m=3.926991 x[-5;5] z[0;50]; неподд=0 сост=1; строк=1 «material M: <composite> G4_WATER is not an element» |
| 10 | `таблица G4` | строк 34, наибольшая /Σ−1/ = 0.056 | строк 34, наибольшая /Σ−1/ = 1E-06 |
| 10 | `сверка с G4NistMaterialBuilder.cc` | сверено строк 34 | сверено строк 34 |
| 11 | `30_tess_open` | A: V=166.6667 m=449.8167 x[0;100] z[0;100] {Al 1.0000}; строк=0 | A: V=? m=? x[0;100] z[0;100] {Al 1.0000}; строк=2 «tessellated P: mesh is not closed: 6 unpaired edge(s), volume is not defined» |
| 11 | `a_tess_closed (было OK)` | A: V=166.6667 m=449.8167 x[0;100] z[0;100] {Al 1.0000}; строк=0 | A: V=166.6667 m=449.8333 x[0;100] z[0;100] {Al 1.0000}; строк=0 |
| 11 | `c4_rmin_gt_rmax` | A: V=47.12389 m=127.1827 x[-10;10] z[0;50] {Al 1.0000}; строк=0 | A: V=? m=? габарит ? {Al 1.0000}; строк=3 «polycone P: rmin = 20, rmax = 10 at z = 0 is not a ring» |
| 11 | `c5_neg_box` | A: V=? m=? x[-5;5] z[-5;5] {Al 1.0000}; строк=0 | A: V=? m=? габарит ? {Al 1.0000}; строк=1 «box P: size -10 x 10 x 10 is not positive» |
| 11 | `c11_density_nan` | A: V=3.926991 m=? x[-5;5] z[0;50] {Fe 1.0000}; строк=0 | A: V=3.926991 m=? x[-5;5] z[0;50] {Fe 1.0000}; строк=1 «material M: D = "NaN" is not a finite number or a known expression» |
| 11 | `c12_density_neg` | A: V=3.926991 m=-11.78097 x[-5;5] z[0;50] {Fe 1.0000}; строк=0 | A: V=3.926991 m=? x[-5;5] z[0;50] {Fe 1.0000}; строк=1 «material M: density -3 is not positive; mass is not computed» |
| 11 | `c13_fraction_nan` | A: V=3.926991 m=11.78097 x[-5;5] z[0;50] {Fe NaN}; строк=0 | A: V=3.926991 m=11.78097 x[-5;5] z[0;50]; неподд=0 сост=1; строк=1 «material M: fraction of G4_Fe = "NaN" is not a non-negative number» |
| 11 | `a_deltaphi_bad` | A: V=7.853982 m=21.19711 x[-5;5] z[0;50] {Al 1.0000}; строк=1 «polycone P: partial revolution 720 deg» | A: V=3.926991 m=10.59895 x[-5;5] z[0;50] {Al 1.0000}; строк=1 «polycone P: deltaphi = "720" is read as a full revolution, as in Geant4» |
| 11 | `a_deltaphi_missing` | A: V=3.926991 m=10.59856 x[-5;5] z[0;50] {Al 1.0000}; строк=0 | A: V=3.926991 m=10.59895 x[-5;5] z[0;50] {Al 1.0000}; строк=1 «polycone P: deltaphi is missing; read as a full revolution, as in Geant4» |
| 11 | `yf1` | A: V=0.05483118 m=0.1479839 x[-10;10] z[0;10] {Al 1.0000}; строк=1 «polycone X: partial revolution 6.28319 deg» | A: V=3.141593 m=8.479159 x[-10;10] z[0;10] {Al 1.0000}; строк=1 «polycone X: deltaphi = "6.28319" is read as a full revolution, as in Geant4» |
| 11 | `yf5` | A: V=? m=? x[-10;10] z[0;10] {Al 1.0000}; строк=1 «polycone X: partial revolution 0 deg» | A: V=3.141593 m=8.479159 x[-10;10] z[0;10] {Al 1.0000}; строк=1 «polycone X: deltaphi = "0" is read as a full revolution, as in Geant4» |
| 11 | `yf6` | A: V=0.008726646 m=0.02355235 x[-10;10] z[0;10] {Al 1.0000}; строк=1 «polycone X: partial revolution -1 deg» | A: V=3.141593 m=8.479159 x[-10;10] z[0;10] {Al 1.0000}; строк=1 «polycone X: deltaphi = "-1" is read as a full revolution, as in Geant4» |
| 11 | `yf7` | A: V=3.141593 m=8.478844 x[-10;10] z[0;10] {Al 1.0000}; строк=1 «polycone X: partial revolution 360 deg» | A: V=3.141593 m=8.479159 x[-10;10] z[0;10] {Al 1.0000}; строк=1 «polycone X: deltaphi = "360.0000001" is read as a full revolution, as in Geant4» |
| 11 | `yf3` | A: V=0.05483113 m=0.1479837 x[-10;10] z[0;10] {Al 1.0000}; строк=1 «polycone X: partial revolution 6.28319 deg» | A: V=3.141593 m=8.479158 x[-10;10] z[0;10] {Al 1.0000}; строк=1 «polycone X: partial revolution 359.99998 deg» |
| 11 | `y15_gp_self` | A: V=? m=? x[-10;10] z[0;10] {Al 1.0000}; строк=1 «genericPolycone X: partial revolution 0 deg» | A: V=? m=? x[-10;10] z[0;10] {Al 1.0000}; строк=2 «genericPolycone X: profile crosses itself» |
| 11 | `y14_gp_cw` | A: V=? m=? x[-10;10] z[0;10] {Al 1.0000}; строк=1 «genericPolycone X: partial revolution 0 deg» | A: V=3.141593 m=8.479159 x[-10;10] z[0;10] {Al 1.0000}; строк=1 «TWOPI is not defined in the file: its usual value is taken, Geant4 would refuse the f… |
| 11 | `e04_dup_vertex_1e-12 (было OK)` | A: V=0.1666667 m=0.4498167 x[0;10] z[0;10] {Al 1.0000}; строк=0 | A: V=0.1666667 m=0.4498333 x[0;10] z[0;10] {Al 1.0000}; строк=0 |
| 11 | `e03_dup_vertex_exact (было OK)` | A: V=0.1666667 m=0.4498167 x[0;10] z[0;10] {Al 1.0000}; строк=0 | A: V=0.1666667 m=0.4498333 x[0;10] z[0;10] {Al 1.0000}; строк=0 |
| 11 | `e1_relative_nondyadic` | A: V=? m=? x[0.1;0.8] z[0.3;1] {Al 1.0000}; строк=2 «tessellated X: 2 face(s) skipped» | A: V=5.716667E-05 m=0.0001542928 x[0.1;0.8] z[0.3;1] {Al 1.0000}; строк=1 «TWOPI is not defined in the file: its usual value is taken, Geant4 would re… |
| 11 | `a_dup_vertex_1e-8` | A: V=166.6667 m=449.8167 x[0;100] z[0;100] {Al 1.0000}; строк=0 | A: V=? m=? x[0;100] z[0;100] {Al 1.0000}; строк=1 «tessellated P: mesh is not closed: 4 unpaired edge(s), volume is not defined» |
| 11 | `e05_relative_lower` | A: V=? m=? габарит ? {Al 1.0000}; строк=2 «tessellated X: 1 face(s) skipped» | A: V=? m=? x[0;10] z[0;0] {Al 1.0000}; строк=2 «tessellated X: type="relative" is read as ABSOLUTE, as in Geant4» |
| 11 | `e07_face_lunit_bad` | A: V=? m=? x[0;10] z[0;0] {Al 1.0000}; строк=0 | A: V=? m=? габарит ? {Al 1.0000}; строк=3 «tessellated X: unknown unit "XX"» |
| 11 | `a_zplane_unordered` | A: V=1.570796 m=4.239422 x[-5;5] z[0;50] {Al 1.0000}; строк=0 | A: V=? m=? габарит ? {Al 1.0000}; строк=1 «polycone P: z-planes are not ordered along z» |
| 11 | `a_position_missing` | A: V=3.926991 m=10.59856 x[-5;5] z[0;50] {Al 1.0000}; строк=1 «physvol pv_A: position ghost not found» | A: V=3.926991 m=10.59895 габарит ? {Al 1.0000}; строк=1 «physvol pv_A: position ghost not found; placement is not known» |
| критик | `p03_zero_div` | A: V=3.926991 m=10.59856 x[-5;5] z[0;50] {Al 1.0000}; строк=0 | A: V=3.926991 m=10.59895 габарит ? {Al 1.0000}; строк=1 «physvol pa: x = "1/(1/0)" is not a finite number or a known expression» |
| критик | `a_div_overflow` | A: V=? m=? x[0;0] z[-5;5] {Al 1.0000}; строк=0 | A: V=? m=? габарит ? {Al 1.0000}; строк=1 «box P: x = "1/(1e308*10)" is not a finite number or a known expression» |
| критик | `n10_expr_zero_div` | A: V=? m=? x[0;0] z[-5;5] {Al 1.0000}; строк=0 | A: V=? m=? габарит ? {Al 1.0000}; строк=1 «box X: x = "1/(1/0)" is not a finite number or a known expression» |
| критик | `t1_material_twice` | A: V=? m=? x[-5;5] z[0;50]; строк=2 «material M: density only, composition is unknown» | A: V=3.926991 m=3.926991 x[-5;5] z[0;50] {Fe 1.0000}; строк=2 «material M is defined twice; the first one is read, as in Geant4» |
| критик | `a_element_twice` | A: V=3.926991 m=3.926991 x[-5;5] z[0;50]; неподд=0 сост=1; строк=3 «materials: unsupported <element>» | A: V=3.926991 m=3.926991 x[-5;5] z[0;50] {Fe 1.0000}; строк=1 «element E is defined twice; the first one is read, as in Geant4» |
| критик | `a_isotope_twice` | A: V=3.926991 m=3.926991 x[-5;5] z[0;50]; неподд=0 сост=1; строк=4 «materials: unsupported <isotope>» | A: V=3.926991 m=3.926991 x[-5;5] z[0;50] {U 1.0000}; строк=1 «isotope I is defined twice; the first one is read, as in Geant4» |
| критик | `a_solid_twice` | A: V=8 m=21.5912 x[-10;10] z[-10;10] {Al 1.0000}; строк=0 | A: V=1 m=2.699 x[-5;5] z[-5;5] {Al 1.0000}; строк=1 «solid X is defined twice; the first one is read, as in Geant4» |
| критик | `a_volume_twice` | A: V=3.926991 m=44.57135 x[-5;5] z[0;50] {Pb 1.0000}; строк=0 | A: V=3.926991 m=10.59895 x[-5;5] z[0;50] {Al 1.0000}; строк=1 «volume A is defined twice; the first one is read, as in Geant4» |
| критик | `t2_position_twice` | A: V=? m=? x[-5;5] z[0;50] {Al 1.0000}; строк=4 «define/position q: coordinates are not numbers» | A: V=3.926991 m=10.59895 x[-5;5] z[99;149] {Al 1.0000}; строк=2 «position q is defined twice; the last one is read, as in Geant4» |
| критик | `a_rotation_twice` | A: V=3.926991 m=10.59856 x[-5;5] z[0;50] {Al 1.0000}; строк=3 «define/rotation r: angles are not numbers» | A: V=3.926991 m=10.59895 x[-50;3.061516E-16] z[-5;5] {Al 1.0000}; строк=1 «rotation r is defined twice; the last one is read, as in Geant4» |
| критик | `d07_const_redefined` | A: V=2 m=5.3978 x[-10;10] z[-5;5] {Al 1.0000}; строк=0 | A: V=? m=? габарит ? {Al 1.0000}; строк=2 «define/constant L: this name belongs to a unit or a constant, Geant4 refuses it; what uses…» |
| критик | `a_const_same` | A: V=1 m=2.6989 x[-5;5] z[-5;5] {Al 1.0000}; строк=0 | A: V=1 m=2.699 x[-5;5] z[-5;5] {Al 1.0000}; строк=1 «define/constant QQ is defined twice with the same value; Geant4 refuses such a file» |
| критик | `d09_shadow_unit` | A: V=? m=? x[0;0] z[-5;5] {Al 1.0000}; строк=0 | A: V=? m=? габарит ? {Al 1.0000}; строк=2 «define/constant cm: this name belongs to a unit or a constant, Geant4 refuses it; what use…» |
| критик | `t06_composite_zero` | A: V=3.926991 m=3.926991 x[-5;5] z[0;50]; неподд=0 сост=1; строк=2 «material M: <composite> read as a mass fraction (atom fractions are not converted)… | A: V=3.926991 m=3.926991 x[-5;5] z[0;50]; неподд=0 сост=1; строк=1 «material M: composition is empty» |
| критик | `t05_fraction_zero` | A: V=3.926991 m=3.926991 x[-5;5] z[0;50] {Fe 0.0000}; строк=0 | A: V=3.926991 m=3.926991 x[-5;5] z[0;50] {Fe 0.0000}; неподд=0 сост=1; строк=1 «material M: composition is empty» |
| критик | `s05_vol_twice` | томов 0; строк=0 | томов 0; строк=2 «volume Top is defined twice; the first one is read, as in Geant4» |
| критик | `s02_two_setups_nodefault` | A: V=3.926991 m=10.59856 x[-5;5] z[0;50] {Al 1.0000}; строк=0 | A: V=3.926991 m=10.59895 x[-5;5] z[0;50] {Al 1.0000}; строк=1 «setup: there are several setups and none is Default; setup O1 is read» |
| критик | `s1_second_empty` | A: V=0.001 m=0.0026989 x[-0.5;0.5] z[-0.5;0.5] {Al 1.0000}; строк=2 «solid X: reference  not found» | A: V=? m=? x[-0.5;0.5] z[-0.5;0.5] {Al 1.0000}; строк=2 «subtraction X: an empty reference to a body» |
| критик | `s2_volume_noname` | A: V=? m=? x[-5;5] z[0;50] {Al 1.0000}; строк=1 «polycone P: partial revolution 0 deg» | A: V=3.926991 m=10.59895 x[-5;5] z[0;50] {Al 1.0000}; строк=2 «structure: <volume> without a name is skipped» |
| критик | `t14_mat_noname` | A: V=3.926991 m=? x[-5;5] z[0;50]; строк=0 | A: V=3.926991 m=? x[-5;5] z[0;50]; строк=2 «materials: <material> without a name is skipped» |
| критик | `v01_no_solidref` | A: V=? m=? габарит ? {Al 1.0000}; неподд=1 сост=0; строк=1 «volume A: solid  not found» | A: V=? m=? габарит ? {Al 1.0000}; неподд=1 сост=0; строк=1 «volume A: no <solidref>» |
| критик | `v02_no_materialref` | A: V=3.926991 m=? x[-5;5] z[0;50]; строк=0 | A: V=3.926991 m=? x[-5;5] z[0;50]; строк=1 «volume A: no <materialref>» |
| критик | `w02_child_outside` | A: V=1 m=2.6989 x[-5;5] z[-5;5] {Al 1.0000}; C: V=8 m=62.992 x[-10;10] z[-10;10] {Fe 1.0000}; строк=0 | A: V=1 m=? x[-5;5] z[-5;5] {Al 1.0000}; C: V=8 m=62.992 x[490;510] z[-10;10] {Fe 1.0000}; строк=1 «volume A: children take 8 cm3 of its 1 cm3: in the … |
| критик | `w05_almost_fill_over2` | A: V=1 m=0.00120479 x[-5;5] z[-5;5] {N 0.7553, O 0.2318, Ar 0.0128}; C: V=1 m=7.874 x[-5;5] z[-5;5] {Fe 1.0000}; строк=0 | A: V=1 m=? x[-5;5] z[-5;5] {C 0.0001, N 0.7553, O 0.2318, Ar 0.0128}; C: V=1 m=7.874 x[-5;5] z[-5;5] {Fe 1.0000}; строк=1 «volume A: children take 1.0… |
| третий | `o10a_const_twopi` | L1: V=? m=? x[0;0] z[0;0] {Fe 1.0000}; L2: V=? m=? x[-5;5] z[0;10] {Fe 1.0000}; строк=2 «define: unsupported <expression>» | L1: V=1.933333 m=15.22307 x[95;105] z[-4.833333;4.833333] {Fe 1.0000}; L2: V=0.7853982 m=6.184225 x[-105;-95] z[0;10] {Fe 1.0000}; строк=0 |
| третий | `a_entity` | fatal | fatal |
| третий | `o05_tess_inverted` | L1: V=1 m=7.874 x[0;10] z[0;10] {Fe 1.0000}; строк=0 | L1: V=? m=? x[100;110] z[0;10] {Fe 1.0000}; строк=1 «tessellated T1: the faces are turned inside out (negative volume); volume is not defined» |
| третий | `e02_flipped_face` | A: V=0.1666667 m=0.4498167 x[0;10] z[0;10] {Al 1.0000}; строк=0 | A: V=? m=? x[0;10] z[0;10] {Al 1.0000}; строк=1 «tessellated X: some faces are turned inside out: 6 edge passes run the same way; volume is…» |
| третий | `30_tess_open` | A: V=166.6667 m=449.8167 x[0;100] z[0;100] {Al 1.0000}; строк=0 | A: V=? m=? x[0;100] z[0;100] {Al 1.0000}; строк=2 «tessellated P: mesh is not closed: 6 unpaired edge(s), volume is not defined» |
| третий | `a_root_extension` | fatal | A: V=3.926991 m=10.59895 x[-5;5] z[10;60] {Al 1.0000}; строк=2 «root element is <gdml_simple_extension>; read as <gdml>, as in Geant4» |
| третий | `a_hex_names` | detLV0x8f9550: V=3.926991 m=? x[-5;5] z[0;50]; неподд=0 сост=1; строк=1 «material G4_Al0x2233: not found in the file or in the built-in G4 table» | detLV: V=3.926991 m=10.59895 x[-5;5] z[0;50] {Al 1.0000}; строк=0 |
| третий | `a_loop_opt` | A: V=3.926991 m=10.59856 x[-5;5] z[0;50] {Al 1.0000}; строк=2 «solid : unsupported body <loop>» | A: V=3.926991 m=? x[-5;5] z[0;50] {Al 1.0000}; строк=3 «solids: <loop> is not read (i from 1 to 3): what it creates is missing» |
| третий | `o02_partial_phi` | L1: V=17.80236 m=140.1758 x[-40;40] z[0;20] {Fe 1.0000}; L2: V=0.1178097 m=0.9276338 x[-20;20] z[0;30] {Fe 1.0000}; строк=2 «polycone P1: partial revo… | L1: V=17.80236 m=140.1758 x[80;134.641] z[0;20] {Fe 1.0000}; L2: V=6.75 m=53.1495 x[-99.29263;-80] z[0;30] {Fe 1.0000}; строк=2 «polycone P1: partial … |
| третий | `a_scale_near1` | A: V=3.926991 m=10.59856 x[-5;5] z[0;50] {Al 1.0000}; строк=0 | A: V=3.926991 m=10.59895 x[0;10] z[0;50] {Al 1.0000}; строк=1 «<scale> 1.00004 1.00004 1.00004: the size is dropped and only the signs are read, as in… |
| третий | `a_scale_ref` | A: V=3.926991 m=10.59856 x[-5;5] z[0;50] {Al 1.0000}; строк=1 «define: unsupported <scale>» | A: V=3.926991 m=10.59895 x[-5;5] z[-50;0] {Al 1.0000}; строк=1 «physvol pv_A: <scale> 1 1 -1 is read as a mirror» |
| третий | `a_scale_half_mirror` | A: V=3.926991 m=10.59856 x[-5;5] z[0;50] {Al 1.0000}; строк=0 | A: V=3.926991 m=10.59895 габарит ? {Al 1.0000}; строк=1 «physvol pv_A: <scale> 0.5 1 -1 is not a mirror and is not applied; placement is not known» |
| третий | `a_tube` | A: V=? m=? габарит ? {Al 1.0000}; неподд=1 сост=0; строк=1 «solid P: unsupported body <tube>» | A: V=4.712389 m=12.71874 x[-10;10] z[-10;10] {Al 1.0000}; строк=0 |
| третий | `a_tube_rad_cm` | A: V=? m=? габарит ? {Al 1.0000}; неподд=1 сост=0; строк=1 «solid P: unsupported body <tube>» | A: V=6.283185 m=16.95832 x[-10;10] z[-10;10] {Al 1.0000}; строк=0 |
| третий | `a_tube_phi` | A: V=? m=? габарит ? {Al 1.0000}; неподд=1 сост=0; строк=1 «solid P: unsupported body <tube>» | A: V=12.56637 m=33.91663 x[-15;25.98076] z[-10;10] {Al 1.0000}; строк=1 «tube P: partial revolution 90 deg» |
| третий | `a_tube_nophi` | A: V=? m=? габарит ? {Al 1.0000}; неподд=1 сост=0; строк=1 «solid P: unsupported body <tube>» | A: V=? m=? габарит ? {Al 1.0000}; строк=1 «tube P: deltaphi is missing» |
| третий | `a_tube_badradii` | A: V=? m=? габарит ? {Al 1.0000}; неподд=1 сост=0; строк=1 «solid P: unsupported body <tube>» | A: V=? m=? габарит ? {Al 1.0000}; строк=1 «tube P: rmin = 10, rmax = 10 at z = -10 is not a ring» |
| третий | `a_cone` | A: V=? m=? габарит ? {Al 1.0000}; неподд=1 сост=0; строк=1 «solid P: unsupported body <cone>» | A: V=21.99115 m=59.35411 x[-20;20] z[-15;15] {Al 1.0000}; строк=0 |
| третий | `a_cone_hole_phi` | A: V=? m=? габарит ? {Al 1.0000}; неподд=1 сост=0; строк=1 «solid P: unsupported body <cone>» | A: V=8.246681 m=22.25779 x[-20;20] z[-15;15] {Al 1.0000}; строк=1 «cone P: partial revolution 180 deg» |
| третий | `a_cone_tip` | A: V=? m=? габарит ? {Al 1.0000}; неподд=1 сост=0; строк=1 «solid P: unsupported body <cone>» | A: V=12.56637 m=33.91663 x[-20;20] z[-15;15] {Al 1.0000}; строк=0 |
| третий | `a_union_disjoint` | A: V=? m=? габарит ? {Al 1.0000}; неподд=1 сост=0; строк=1 «solid U: unsupported body <union>» | A: V=2 m=5.398 x[-5;105] z[-5;5] {Al 1.0000}; строк=0 |
| третий | `a_union_boxes` | A: V=? m=? габарит ? {Al 1.0000}; неподд=1 сост=0; строк=1 «solid U: unsupported body <union>» | A: V=1.5 m=4.0485 x[-5;10] z[-5;5] {Al 1.0000}; строк=0 |
| третий | `a_union_inside` | A: V=? m=? габарит ? {Al 1.0000}; неподд=1 сост=0; строк=2 «solid T: unsupported body <tube>» | A: V=1000 m=2699 x[-50;50] z[-50;50] {Al 1.0000}; строк=0 |
| третий | `a_union_overlap` | A: V=? m=? габарит ? {Al 1.0000}; неподд=1 сост=0; строк=3 «solid T1: unsupported body <tube>» | A: V=? m=? x[-10;15] z[-10;10] {Al 1.0000}; строк=1 «union U: overlap of T1 and T2 is not computed: their extents meet and they are not axis-al…» |
| третий | `a_union_nested` | A: V=? m=? габарит ? {Al 1.0000}; неподд=1 сост=0; строк=3 «solid T: unsupported body <tube>» | A: V=2.785398 m=7.51779 x[-105;105] z[-5;5] {Al 1.0000}; строк=0 |
| третий | `a_union_rot` | A: V=? m=? габарит ? {Al 1.0000}; неподд=1 сост=0; строк=1 «solid U: unsupported body <union>» | A: V=12 m=32.388 x[-5;110] z[-15;15] {Al 1.0000}; строк=0 |
| третий | `a_union_first_pos` | A: V=? m=? габарит ? {Al 1.0000}; неподд=1 сост=0; строк=1 «solid U: unsupported body <union>» | A: V=2 m=5.398 x[-5;5] z[-5;5] {Al 1.0000}; строк=0 |
| третий | `a_union_unknown (было OK)` | A: V=? m=? габарит ? {Al 1.0000}; неподд=1 сост=0; строк=2 «solid S: unsupported body <sphere>» | A: V=? m=? габарит ? {Al 1.0000}; неподд=1 сост=0; строк=1 «solid S: unsupported body <sphere>» |
| третий | `a_union_in_sub` | A: V=? m=? x[-50;50] z[-50;50] {Al 1.0000}; строк=1 «solid U: unsupported body <union>» | A: V=998 m=2693.602 x[-50;50] z[-50;50] {Al 1.0000}; строк=0 |
| третий | `a_union_cycle` | A: V=? m=? габарит ? {Al 1.0000}; неподд=1 сост=0; строк=2 «solid U: unsupported body <union>» | A: V=? m=? габарит ? {Al 1.0000}; строк=1 «solid U: reference cycle» |
| контроль | `a_ctrl_clean` | A: V=3.926991 m=10.59856 x[-5;5] z[0;50] {Al 1.0000}; строк=0 | A: V=3.926991 m=10.59895 x[-5;5] z[10;60] {Al 1.0000}; строк=0 |
| контроль | `02_tube_hole (было OK)` | A: V=18.84956 m=50.87307 x[-20;20] z[-10;10] {Al 1.0000}; строк=0 | A: V=18.84956 m=50.87495 x[-20;20] z[-10;10] {Al 1.0000}; строк=1 |
| контроль | `27_cup (было OK)` | A: V=19.50301 m=52.63667 x[-30;30] z[0;40] {Al 1.0000}; строк=0 | A: V=19.50301 m=52.63862 x[-30;30] z[0;40] {Al 1.0000}; строк=1 |
| контроль | `35_partial_phi (было OK)` | A: V=15.70796 m=42.39422 x[-20;20] z[0;50] {Al 1.0000}; строк=1 «polycone P: partial revolution 90 deg» | A: V=15.70796 m=42.39579 x[0;20] z[0;50] {Al 1.0000}; строк=2 «polycone P: partial revolution 90 deg» |
| контроль | `37_ctrl_gp (было OK)` | A: V=9.424778 m=25.43653 x[-20;20] z[0;10] {Al 1.0000}; строк=0 | A: V=9.424778 m=25.43748 x[-20;20] z[0;10] {Al 1.0000}; строк=1 |
| контроль | `38_ctrl_exp (было OK)` | A: V=1 m=2.6989 x[-5;5] z[-5;5] {Al 1.0000}; строк=0 | A: V=1 m=2.699 x[-5;5] z[-5;5] {Al 1.0000}; строк=1 |
| настоящие | `GS2020_Capescint51x51` | мс=1311 томов=48 веществ=11 неподд=0 сост=0 строк=0 | мс=1166 томов=48 веществ=11 неподд=0 сост=0 строк=3 |
| настоящие | `GS2020_marinelli_1000ml_water` | мс=1264 томов=51 веществ=14 неподд=0 сост=0 строк=0 | мс=1218 томов=51 веществ=14 неподд=0 сост=0 строк=3 |
| настоящие | `GS2020_marinelli_berry_dry_1000ml` | мс=1507 томов=51 веществ=14 неподд=0 сост=0 строк=0 | мс=1357 томов=51 веществ=14 неподд=0 сост=0 строк=3 |
| настоящие | `GS2020_marinelli_kcl_1000ml` | мс=1943 томов=51 веществ=14 неподд=0 сост=0 строк=0 | мс=1567 томов=51 веществ=14 неподд=0 сост=0 строк=3 |
| настоящие | `GS2020_marinelli_th232` | мс=2328 томов=50 веществ=13 неподд=0 сост=0 строк=0 | мс=1145 томов=50 веществ=13 неподд=0 сост=0 строк=3 |
| настоящие | `RC103_detector` | мс=0 томов=20 веществ=12 неподд=0 сост=0 строк=0 | мс=1 томов=20 веществ=12 неподд=0 сост=0 строк=1 |
| настоящие | `geometry_marinelli1l_oisn10` | мс=1411 томов=51 веществ=14 неподд=0 сост=0 строк=0 | мс=1242 томов=51 веществ=14 неподд=0 сост=0 строк=3 |
| примеры | `G01/assembly.gdml (было OK)` | томов=0 с объёмом=0 строк=11 | томов=0 с объёмом=0 строк=2 |
| примеры | `G01/auxiliary.gdml (было OK)` | томов=1 с объёмом=1 строк=4 | томов=1 с объёмом=1 строк=0 |
| примеры | `G01/axes.gdml (было OK)` | томов=7 с объёмом=7 строк=22 | томов=7 с объёмом=4 строк=1 |
| примеры | `G01/block.gdml (было OK)` | томов=0 с объёмом=0 строк=11 | томов=0 с объёмом=0 строк=2 |
| примеры | `G01/divisionvol.gdml (было OK)` | томов=0 с объёмом=0 строк=4 | томов=1 с объёмом=1 строк=1 |
| примеры | `G01/loop.gdml (было OK)` | томов=0 с объёмом=0 строк=6 | томов=0 с объёмом=0 строк=3 |
| примеры | `G01/multiUnion.gdml (было OK)` | томов=1 с объёмом=0 строк=6 | томов=1 с объёмом=0 строк=1 |
| примеры | `G01/opticalsurfaces.gdml (было OK)` | томов=2 с объёмом=2 строк=15 | томов=2 с объёмом=2 строк=5 |
| примеры | `G01/pTube.gdml (было OK)` | томов=2 с объёмом=2 строк=9 | томов=2 с объёмом=2 строк=2 |
| примеры | `G01/parameterized.gdml (было OK)` | томов=2 с объёмом=2 строк=8 | томов=2 с объёмом=2 строк=1 |
| примеры | `G01/replicated.gdml (было OK)` | томов=0 с объёмом=0 строк=4 | томов=1 с объёмом=1 строк=1 |
| примеры | `G01/scale.gdml (было OK)` | томов=20 с объёмом=4 строк=5 | томов=20 с объёмом=4 строк=8 |
| примеры | `G01/scaledSolids.gdml (было OK)` | томов=16 с объёмом=0 строк=35 | томов=16 с объёмом=0 строк=28 |
| примеры | `G01/scaledTube.gdml (было OK)` | томов=20 с объёмом=2 строк=7 | томов=20 с объёмом=2 строк=9 |
| примеры | `G01/solids.gdml (было OK)` | томов=26 с объёмом=4 строк=96 | томов=26 с объёмом=6 строк=22 |
| примеры | `G01/tess.gdml (было OK)` | томов=2 с объёмом=2 строк=4 | томов=2 с объёмом=2 строк=0 |
| примеры | `G02/test.gdml (было OK)` | томов=0 с объёмом=0 строк=17 | томов=23 с объёмом=23 строк=7 |
| примеры | `G03/color_extension.gdml (было OK)` | томов=0 с объёмом=0 строк=0 отказ | томов=1 с объёмом=1 строк=3 |
| примеры | `G04/auxiliary.gdml (было OK)` | томов=1 с объёмом=1 строк=4 | томов=1 с объёмом=1 строк=0 |

## Настоящие 8 файлов

Третий заход: V, масса и габариты всех томов побитно те же (дамп `real_prev2.tsv` против `real_new.tsv`, md5 совпал); изменился только показ состава в 19 строках `RC103`, `RC110` от нормирования Н9 (ESR 0.333 → 0.3333, воздух N 0.7553 → 0.7554).

Тела и вещества те же: тома (48, 51, 51, 51, 50, 20, 19, 51), вещества (11, 14, 14, 14, 13, 12, 10, 14), неподдержанных 0, неизвестных составов 0, состав томов и габариты без изменений (`bounds` 0 строк). Время чтения `GS2020_marinelli_th232` (12 МБ), лучшее из пяти, сборка без оптимизации, новый и старый поочерёдно, четыре захода, машина под чужим счётом: **1408, 1108, 1287, 1242 мс против 1169, 964, 984, 1017** (не хуже ×1.31; на свободной машине до третьего захода 311, 364, 321, 311 против 320, 363, 334, 325). Ось: GS2020 — z; `RC103`, `RC110` — x, как и было.

Изменились:

| что | где | было → стало |
|---|---|---|
| масса `LV_VesselWall` | вода, ягода, KCl (3 файла) | 1370.288 → **251.3351** г (×5.452) |
| масса `LV_VesselWall` | `th232`, `oisn10` | 1363.672 → **171.666** г (×7.944) |
| объём и масса `LV_7175025104_1_31`, `_2_32`, `LV_Open_CASCADE_…_33` | 6 файлов GS2020 | 0.03558, 0.01496, 3.7596 см3 → **не измерено**; 168, 168, 104 непарных ребра — строка сводки |
| плотность алюминия | все GS2020 | 2.6989 → 2.699, масса ×1.000037 |
| сталь `…Phillips_35`…`_40` | 6 файлов GS2020 | ρ 8.02 → 8.00, Si и Mn ушли, Ni 0.12 → 0.0848, масса ×0.9975 |
| `LV_Headspace` | 4 файла с `G4_AIR` из таблицы | появился C 0.0001 |
| `RC103_device_log`, `Case_interior_log`, `Capsule_cavity_log` | `RC103` | 0.08817 → 0, 0.06499 → 0.04496, 0.00152 → 0.000238 г |
| `DetectorModule_log` | `RC103` | 0.00401 г → **`?`**, строка: дети 3.38749 см3 при своём 3.328 см3 |
| `RC110_device_log`, `Case_interior_log`, `DetectorModule_log`, `Capsule_cavity_log` | `RC110` | 0.11287 → 0, 0.08660 → 0.05519, 0.00703 → 0, 0.00407 → 0.000635 г |

Строки `Warnings`: 0 → 3 в шести файлах GS2020, 0 → 1 в `RC103`, 0 → 0 в `RC110`.

## Передача следующей полосе (`GdmlSketch`, `AMBER292`)

* Какие значения идут дальше: читатель пропускает конечные огромные числа — `16c`, `16f`, `16g`, `16h` (коробка 1e300, 1e38, 1e39 мм), `c1b`, `c1c`. Прогон `OnPaint` отражением на `Bitmap` (`RevBProbe --full`) с чертежом полосы `fixGdmlB`: по всем входам, 8 настоящим файлам и `cases_crash*` исключений нет. Неконечного в `Min`, `Max`, `Transform` читатель больше не отдаёт.
* Вычитание: положения в `GdmlSolid.SecondPlacements` (вровень с `Second`) и `FirstPlacement` (null — на месте).
* `GdmlGeometry.DetectAxis` теперь берёт образ локальной z (`GdmlTransform.ImageOfLocalZ`): заголовок торца и штрих оси на `34_pc_rot_end` идут за ней.
* Новые поля без чертежа в строке сводки: `GdmlVolumeInstance.OwnVolumeCm3`, `MassNote`, `PlacementKnown`; `GdmlMaterial.AtomicMass`; `GdmlSolid.Unreadable`, `ContainsUnknown`. `GdmlGeometry.MaxInstances`. Если том не построен из-за предела, `Volumes` пуст, и рисунок покажет `GdmlSketchNoGeometry`, а причина — в сводке.

## Ключи ресурсов

Ключи первой сдачи и второго захода внесла полоса `fixGdmlB`. 12 ключей третьего захода (`GdmlFatalDtd`, `GdmlReasonCsgZ`, `GdmlReasonMeshFlipped`, `GdmlReasonMeshInverted`, `GdmlReasonUnionOverlap`, `GdmlSketchUnion`, `GdmlSummaryUnion`, `GdmlWarnConstantAssumed`, `GdmlWarnLoop`, `GdmlWarnOpticalSurface` и другие, полный список: `D:\BqMoni_Claude\critGdml5\keys.txt`) внесены в `Resources*` вне этой полосы; критик `critGdml5` сверил EN с запасным текстом кода и плейсхолдеры. Эта полоса `Resources*` не правила. На 05.11.2026 в `GdmlGeometry.cs` нет ни одного ключа без записи в `Resources.resx` и `Resources.ru.resx`, кроме имени класса `GdmlG4Materials`, попавшего под образец. Ключи чертежа `designGdml`: журнал `handover-2026-11-05-design-gdml.md`.

Остаются английскими и без ключа, нарочно: имена тегов и атрибутов GDML в строках (`box P:`, `physvol pv_A:`, `define/position`, `material M:`, `x = "NaN"`) — это слова формата файла, а не интерфейса; `GdmlMaterial.Origin` («file», «G4», «cycle») — служебное, на экран не идёт; `FatalError` из `XmlException` — текст системы. Единицы в строке тома теперь через ключи `GdmlUnit*`.

## `check_all`

`python tools/check_all.py` на дереве с правками других полос: код 1, отказали 6 из 47 (`D:\BqMoni_Claude\fixGdmlA\check_all4.txt`, на диске, не в git). От этой полосы отказов нет: `check_resx_designer.py` и `check_resx*` зелёные.

* `check_fsa_report_view.py`, `check_corpus_scenes.py`, `check_fsa_showcase.py`, `check_bq_summing.py`, `check_chain_cut.py` — код 3: канонический каталог `probes\build` не пересобран после правок дерева, а пробы `FsaChainCutEmptySegmentProbeA309` в нём нет. Эта полоса его не трогала; нужен пересбор распорядителем.
* `check_registry.py` — находки N8 про незакоммиченные журналы других полос.

## Что не сделано и почему

* Тела `intersection`, `sphere` и прочие: вне постановки, идут строкой «неподдержанное тело».
* Функции и степень в выражениях (`sqrt(2)`, `2^3`): строка-отказ с именем тела и значением. Вычислитель покрывает выражения постановки и единицы; расширять без настоящего файла незачем.
* `lunit` на самом `tessellated`: как и Geant4, не применяется, но теперь называется.
* `EndPlane`, `FrameMin`, `FrameMax` при координатах порядка 1e308 могут переполниться в сумме: рисунок проверяет рамку сам, см. выше.

## Ключи, которые больше не читаются кодом

Текст поменял смысл, а `GdmlText.T` отдаёт значение из `resx`, поэтому у новых формулировок новые имена. Снять из `Resources*`: `GdmlReasonDeltaPhi`, `GdmlWarnSolidTwice`, `GdmlWarnVolumeTwice`, `GdmlWarnOverlap`, `GdmlNoteMassOverlap`.

## Дополнение к закрытию `AMBER303` (третий заход)

Сверка с настоящим читателем Geant4 11.4.2 (`critGdml4`, свой `dump.exe`): 8 настоящих файлов, 341 том, `dv 0`, `dpos 0`, `dbb 0`, `dcomp 0`. Занятые имена отделены от значений: ложное «Geant4 refuses» на `TWOPI` было в 107 из 144 файлов, стало 0. `<!ENTITY>` — понятный отказ; сетка наизнанку и вывернутые грани — свои строки и «не измерено»; корень `gdml_simple_extension` читается; хвост `0x…` снят в показе; `loop`, `opticalsurface` названы; габарит неполного оборота как `DiskExtent`; доли нормируются как `G4Material`; `scale` как `G4ReflectionFactory`. Проба 199 входов с `--examples`, 0 расхождений; мутантов 10 из 10 поймано; `GdmlSketchProbeA304` код 0.

## Текст закрытия `AMBER312`

Закрыто 05.11.2026 полосой `fixGdmlA`; Amber вопросником, дословно: «Добавить tube, union, cone (Рекомендую)». `tube` и `cone` читаются полными и с `startphi`, `deltaphi`, `rmin`, `rmin1/rmax1/rmin2/rmax2`, `z`, `lunit`, `aunit`; `union` — `first`/`second` с положениями и поворотами, вложенные. Объём, масса, габарит, сводка; чертёж: сечение и торец, контур объединения — край общей области при любом числе частей (`designGdml`, Н-А критика `critGdml5`), подпись, бюджет `MaxViewVertices`. Неполный оборот: разрез рисуется, когда плоскость идёт через ось (половина или две половины сектора); если сектор плоскость y = 0 не задевает, разрез уходит на середину габарита, плоскость проходит мимо оси и на этом виде стоит подпись «No exact section»; торец рисуется сектором кольца, когда ось тела совпадает с осью вида; иначе подпись. Объём `union` точен при непересекающихся габаритах, вложении в осевую коробку и двух осевых коробках; иначе «не измерено» с причиной, оценка Geant4 не подставляется. Сверка с настоящим Geant4 на 22 примерах `extended/persistency/gdml`: томов 127 → 128 при 273 у Geant4, с объёмом 44 → 53; прочее — неподдержанные теги и тела, строкой. Настоящие 8 файлов: V, масса и габариты побитно те же. Расчёт по импорту закрыт, как и был. Журнал `handover/handover-2026-11-05-fixgdml-a.md`.

## Текст закрытия `AMBER303`

Закрыто 05.11.2026 полосой `fixGdmlA`, правка относится к срезу `939102f7`: журнал `handover/handover-2026-11-05-fixgdml-a.md`, приёмка `GdmlReaderProbeA303` — 199 входов, 0 расхождений, на читателе `e62b2f69` 158 расхождений, в том числе падения 0xC00000FD на цепочках в 200000 ссылок; вердикт критика `critFixGdmlA` учтён. Масса — как `G4LogicalVolume::GetMass` (`LV_VesselWall` th232 1363.67 → 171.67 г, вода 1370.29 → 251.34 г, `DetectorModule_log` RC103 — строка о перекрытии); поворот physvol с обращением, угол без единицы — радиан; незнакомые теги, единицы, неконечные числа, незамкнутая сетка (3 тела в 6 файлах GS2020), `rmin > rmax`, плохая плотность — строка сводки и «не измерено»; вычисление выражений, `quantity`, `setup/world`, положение в `subtraction`, составы `Z`, `element`, `composite`; таблица `G4_*` сверена с `G4NistMaterialBuilder.cc` (сверх постановки — `G4_Al`, `G4_N`). Читательская часть `AMBER292` тем же заходом; рисунок `GdmlSketch` — полосы `fixGdmlB` и `designGdml`. Строки ресурсов внесены вне этой полосы, раздел «Ключи» журнала.
