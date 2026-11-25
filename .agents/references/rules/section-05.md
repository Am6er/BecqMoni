<!-- Контекст Codex; действующие правила — в корневом AGENTS.md. -->

## Build

There is one solution (`BecquerelMonitor.sln`) with one project
(`BecquerelMonitor/BecquerelMonitor.csproj`). Build with MSBuild (VS 2022). The canonical
agent build command writes its outputs and intermediates under D:\BqMoni_Claude. In the examples, replace gdml-review with the directory for your task:

```powershell
& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' `
  'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\BecquerelMonitor\BecquerelMonitor.csproj' `
  /t:Build /p:Configuration=Debug /p:Platform='AnyCPU' `
  /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='D:/BqMoni_Claude/gdml-review/bin/Debug/' `
  /p:IntermediateOutputPath='D:/BqMoni_Claude/gdml-review/obj/Debug/'
```

⛔ **`/p:GenerateManifests=false` is NOT redundant — do not drop it** (`T75`). Without it the
build fails whenever another process holds one of `BecquerelMonitor/*.sqlite` in one of the
modes below: `error MSB3171: Ошибка создания манифеста. Процесс не может получить доступ к
файлу "…\schemedb.sqlite"`. Compilation is not involved — it is the ClickOnce manifest step,
which lists and hashes the three `*.sqlite` as application content (they appear as
`<file name="schemedb.sqlite" size=… hash=…>` in `BecquerelMonitor.exe.manifest`), so one busy
database blocks every build in the tree. Caught 25.08.2026 in a wave of eight agents: eight
attempts 15 s apart, all eight refused.

⚠ **WHICH holder breaks it — separating experiment, 27.08.2026.** One variable at a time,
`/t:Rebuild` into a fresh `OutputPath`, holder = a second `pwsh` calling `File.Open` on
`schemedb.sqlite`, with a positive control on every run (an independent process trying to open
the file, so a holder that silently died cannot be mistaken for a passing build — **it happened:
`Start-Process -ArgumentList` split this repo's spacey path and the first four runs measured
nothing at all**):

| holder access | holder share | build without the key |
|---|---|---|
| `Read` | `ReadWrite` | **exit 0, no MSB3171** |
| `ReadWrite` | `ReadWrite` | exit 1, MSB3171 |
| `Read` | `None` | exit 1, MSB3171 |
| `ReadWrite` | `None` | exit 1, MSB3171 |

So the manifest step needs the file **readable and not write-held**: denying sharing breaks it,
and so does merely holding *write* access while sharing everything. Only a pure reader that
shares read-write is harmless — which is exactly what `python -c sqlite3.connect("file:…?mode=ro")`
is, and why such a reader really does **not** break the build. ⚠ It follows that
**who held the database on 25.08.2026 is still NOT established**: both a sharing-denier and a
plain writer fit the evidence. Do not narrow it further without a new measurement.

With the same `ReadWrite`+`ReadWrite` holder still alive, the recipe **with** the key gives
exit 0 — measured in the same wave.

⚠ **What the key really changes** (measured on two `/t:Rebuild` runs into separate output
dirs): the two ClickOnce files — `BecquerelMonitor.application` and
`BecquerelMonitor.exe.manifest` — are not produced, and `app.manifest` is instead **embedded**
into the exe (`csc … /win32manifest:app.manifest`; in the measured pair the exe differed by
1024 bytes and only the key-built one carried an embedded `requestedExecutionLevel`). The
requested privilege level is `asInvoker` either way, so nothing about running the app or a
probe changes. ⛔ **A RELEASE for the autoupdate at amba.cloud/becqmoni must be built WITHOUT
the key** — those two files are precisely what ClickOnce updating needs.

⛔ **The key needs its own `IntermediateOutputPath` too, not only `OutputPath`** (13.10.2026).
`csc` writes the exe to `obj\<Configuration>\` first, and that directory is shared with the
IDE. A key-built exe left in `obj\Debug\` (`app.manifest` embedded) looked up to date to the
IDE, so the ClickOnce *Publish* of 2026.10.12.01 skipped compilation and shipped it: the update
failed with `RefDefValidation` («Ссылка в манифесте не соответствует удостоверению скачанной
сборки BecquerelMonitor.exe») — ClickOnce took the identity `MyApplication.app 1.0.0.0` from
the embedded manifest instead of `BecquerelMonitor 2026.10.12.1`. Each recipe therefore sets a separate intermediate directory for its configuration. If a publish ever fails like that: **Rebuild** in the
IDE, then Publish.

**There are two recipes, and this is the one for DEBUGGING.** It builds WITHOUT optimization
(`Debug` sets `<Optimize>false</Optimize>`), and the probes compile against it — so everything
built this way *computes* unoptimized too. Measured 17.08.2026 on the heaviest corpus scene:
the response matrix takes **2.09× longer** in `Debug`, and the result is **bit-identical**
(`MatrixDiffProbe`: 0.000 % across all 101 nodes).

### ⚡ Computing — the Release recipe (decision of Amber, 17.08.2026)

**Anything where the wait is the point — matrices, corpus runs, timing A/B — is computed with
the optimized build**, from its own directories. Separate dirs are mandatory: `build_all.ps1`
overwrites whatever `-Out` it is given, and pointing it at the working probe dir would replace
the Debug set.

```powershell
& '...\MSBuild.exe' 'BecquerelMonitor\BecquerelMonitor.csproj' /t:Build `
  /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false `
  /p:GenerateManifests=false /p:OutputPath='D:/BqMoni_Claude/gdml-review/bin/Release/' `
  /p:IntermediateOutputPath='D:/BqMoni_Claude/gdml-review/obj/Release/'
pwsh tools\effmaker\probes\build_all.ps1 -Bin 'D:/BqMoni_Claude/gdml-review/bin/Release' `
                                         -Out 'D:/BqMoni_Claude/gdml-review/probes/Release'
```

⛔ **A fresh `-Out` directory yields probes that build and then die** (`T45`): `build_all.ps1`
copies the app exe, the three `*.sqlite` and the `ru` satellite, but **not** the NuGet
dependencies (`Microsoft.Data.Sqlite`, `SQLitePCLRaw.*`, `SpecUtilsNet.dll`) nor the native
`runtimes\win-x64\native\e_sqlite3.dll`. The compiler stays silent; the first database call
throws `TypeInitializationException` / "Library e_sqlite3 not found". Copy them from the
working `probes\build` (or from `$Bin`) before running anything there.

⛔ **ПЕРЕД СЧЁТОМ ДОЛЬШЕ ПОЛУЧАСА — ПЕРЕСОБРАТЬ ЦЕЛЕВОЙ КАТАЛОГ ПРОБ И УБЕДИТЬСЯ, ЧТО
СБОРКА ПРОШЛА** (решение Amber 03.09.2026, `A77`). Не «запустить пересборку», а
посмотреть на её код возврата: 02.09.2026 `build_all.ps1` честно падал `CS1061`, а прогон
уже шёл — каталогом, собранным ЧАСОМ РАНЬШЕ. Старый разбор увидел новое значение ключа
`--cone=far`, сравнил его с `"0"`, счёл «не ноль» и включил конус ВСЕМ 44 сценам. Ни отказа,
ни предупреждения. ⚠ **Ни побитовый замер, ни клеймо этого не ловят:** содержимое матриц
вышло верным, испорчено ПРОИСХОЖДЕНИЕ — они пометились `cone=on`. Цена — три часа счёта.

Два следствия, оба стоили по прогону:

* ⛔ **Отказавший сторож значит «собирать в ДРУГОЙ каталог», а не «положить файл мимо
  него».** Каталог обязан собираться ТЕМ ЖЕ движением, которым проверяется, иначе сторож
  перестаёт что-либо значить. `Copy-Item` свежего `BecquerelMonitor.exe` в каталог проб
  оставляет состояние «приложение свежее, пробы старые», и его ничем не видно.
* ⚠ **Настройку проверять на ПЕРВЫХ ДВУХ единицах работы, а не на всех.** На второй матрице
  видно ровно то же, что на сорок четвёртой, а цена узнать различается на три часа.

NuGet packages are vendored under `./packages` (see `nuget.config`, `globalPackagesFolder`).
The app depends on a native binding **`SpecUtilsNet.dll`** (SpecUtils C# binding, copied to
output as content) and ships its reference data as **three SQLite files** — `matdb.sqlite`
(matter/transport physics), `schemedb.sqlite` (Geant4 level schemes), `nucdb.sqlite` (nuclides
and cascade coincidences) — via SQLitePCLRaw /
Microsoft.Data.Sqlite). User config lives at `%AppData%\BecqMoni` at runtime.

`AGENTS.md` (gitignored) holds the maintainer's personal agent build instructions; treat it
as authoritative for build specifics if present.
