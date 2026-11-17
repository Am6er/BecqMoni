<!-- Контекст Codex; действующие правила — в корневом AGENTS.md. -->

## Profiling

⛔ **`dotnet-trace` does NOT work here.** It collects through EventPipe, which is .NET Core
3.0+ only; this project is .NET Framework 4.8, so the only route is ETW. Most online advice
about profiling .NET assumes EventPipe and is inapplicable.

**PerfView** is the profiler an agent can drive end to end — ETW-based, knows .NET Framework,
fully command-line, single self-contained exe. Installed 17.08.2026 via
`winget install --id Microsoft.PerfView`; on PATH as `perfview` (a shell started before the
install won't see it — the shim is
`%LOCALAPPDATA%\Microsoft\WinGet\Links\perfview.exe`, call it by full path and stop guessing).

Working recipe, all four parts needed (established 17.08.2026 by walking into each trap):

```powershell
# 1. the load goes into a .cmd on a path WITHOUT SPACES — see the quoting trap below
#    ⛔ такие .cmd, логи, worktree, песочницы и снимки склада — ТОЛЬКО в D:\BqMoni_Claude\<полоса>\
#    (распоряжение Amber 13.09.2026 «D:\BqMoni_Claude\ работайте тут.»), не в C:\Users\moroz\
#    (load.cmd: "<repo>\tools\effmaker\probes\build\CorpusMatrixProbe.exe" --dir=… --nodes=2 …)
# 2. collect
perfview.exe /noGui -AcceptEULA -ThreadTime "/DataFile:$s\low.etl.zip" "/LogFile:$s\c.txt" run "$s\load.cmd"
# 3. export to text — a USER COMMAND, not a flag
perfview.exe /noGui -AcceptEULA "/LogFile:$s\e.txt" UserCommand SaveCPUStacksAsCsv "$s\low.etl.zip" CorpusMatrixProbe
# 4. read $s\low.perfView.csv — Name,Exc,Exc%,Inc,Inc%,…
```

⛔ **Self-elevation LOSES THE QUOTES.** PerfView needs admin (the kernel ETW session won't
start otherwise), checks it itself and relaunches via `/RestartingToElevelate` — and the
relaunched command line is re-split, so a path containing spaces (this repo:
`BQ Eng res .NET 4.8`) breaks with `Could not find command \ on path`. Hence part 1: the load
always goes through a `.cmd` whose own path has no spaces.

⚠ **`/noGui` on EVERY invocation, `/?` and `UserCommand` included** — otherwise a window opens
and a headless run hangs on it.

⚠ **stdout is empty**: PerfView writes everything to `/LogFile:`, including `/?`. To read the
help, capture it into a file. There is no text-export *flag*; the flat profile comes from the
user command above (`UserCommandHelp` lists the rest: `GCStats`, `JITStats`, `SaveCPUStacks`).

⚠ The CSV carries **locale decimal commas** (`12,58698`), so a naive `float()` on it throws.

### ✅ Profiling without a UAC prompt — registered 17.08.2026 by Amber, verified working

✅ **11.11.2026 задачи `BqPerfViewStart`, `BqPerfViewStop`, `BqPerfViewAbort` заведены снова** (07.11.2026 их на машине не было). Amber 11.11.2026 вопросником по `AMBER326`, дословно: «Верни задачи прямо сейчас, не откладывая, я подтвержу!» — это отменяет её «Профиль без PerfView» от 06.11.2026. Завела Amber от администратора скриптом `D:\BqMoni_Claude\perfview_tasks.ps1` (вне git): `Register-ScheduledTask`, пользователь Amber, `RunLevel Highest`, действие — `C:\Program Files\PerfView\PerfView.exe start|stop|abort /noGui /AcceptEULA /DataFile:%LOCALAPPDATA%\bqprof\data.etl /ThreadTime /Merge:TRUE /Zip:TRUE /CircularMB:4000 /LogFile:…`. Проверено в тот же день: старт за 4 с, стоп с упаковкой за 80 с, `SaveCPUStacksAsCsv` дал ~5000 выборок пятисекундной нагрузки. Если задачи снова пропадут — вернуть может только Amber от администратора, тем же скриптом.

Three scheduled tasks at `/rl HIGHEST` do the elevated part, so an agent profiles unattended:

```powershell
schtasks /run /tn BqPerfViewStart      # поднять ETW-сеанс
# ...прогнать нагрузку ОБЫЧНЫМ пользователем, elevation ей не нужен...
schtasks /run /tn BqPerfViewStop       # снять, слить, упаковать
# %LOCALAPPDATA%\bqprof\data.etl.zip ; при зависшем сеансе — BqPerfViewAbort
& "$env:LOCALAPPDATA\Microsoft\WinGet\Links\perfview.exe" /noGui -AcceptEULA `
  "/LogFile:$env:LOCALAPPDATA\bqprof\export.log" UserCommand SaveCPUStacksAsCsv `
  "$env:LOCALAPPDATA\bqprof\data.etl.zip" CorpusMatrixProbe
# читать %LOCALAPPDATA%\bqprof\data.perfView.csv — Name,Exc,Exc%,Inc,Inc%,…
```

Poll `schtasks /query /tn BqPerfViewStop /fo list` for `Status: Ready` — the tasks are
asynchronous, and there is no sentinel file by design (see below). The export needs no rights.
⚠ Две грабли 13.09.2026 (П45): (1) `schtasks /query … | Select-String '^Status:'` в скрытом окне
строку не находит — ждать `(Get-ScheduledTask -TaskName BqPerfViewStop).State -eq 'Ready'`;
(2) `perfview … UserCommand SaveCPUStacks` ВОЗВРАЩАЕТ УПРАВЛЕНИЕ СРАЗУ (отвязывается), файл
`*.perfView.xml.zip` появляется через минуту — ждать строку `[DONE` в `/LogFile:`. И для профиля
двух узлов `CorpusMatrixProbe` обязателен `--jnodes=0`: без него сетка κ пар (9.6 М историй)
съедает 90 % прогона, а «мкс на историю» делит на истории узлов и печатает 26 вместо 2.7.

**Why it is built this way, and do not "simplify" it back.** The elevated action is a *fixed*
command line pointing at a copy of PerfView in `C:\Program Files\PerfView` — a directory only
an administrator can write to. The earlier design (a task whose action was a `.cmd` in the
repo) was refused, and correctly: a file an agent can write, executed by an administrator
without a prompt, is a privilege-escalation path. Note that `%LOCALAPPDATA%\Microsoft\WinGet\`
— both the `Links` shim and the `Packages` binary — is user-writable, so pointing the task
there would reopen exactly that hole. The `run <command>` form of PerfView is therefore not
used at all: `start` / `stop` bracket a machine-wide session, and the load runs unprivileged
in between.

What does NOT help: the *Performance Log Users* group (PerfView judges by token elevation, not
privileges), a home-grown TraceEvent collector (same kernel session), CLR-only user-mode
providers (no CPU samples at all).

⚠ **Still keep the no-profiler fallbacks sharp** — they found more than the profile did on
17.08.2026: the walk counters printed by `CorpusMatrixProbe` ("на историю … шага границ, …
поиска области"), CPU-seconds per history, the ablation keys (`--recollect`, `--scat=`,
`--acont=`, `--bound=`), and the cost of one history at the bottom / middle / top of the scale.

If the text export is not enough, the fallback is the **TraceEvent** library (PerfView is built
on it): a few dozen lines of C# read the `.etl` and print a flat profile, and C# builds are
already routine here.

**`VSDiagnostics.exe`** — Visual Studio's own collector, already installed at
`…\2022\Community\Team Tools\DiagnosticsHub\Collector\`, with ready configs in `AgentConfigs\`
(`CpuUsageHigh.json`, `CpuUsageWithCallCounts.json`, `DotNetObjectAllocBase.json` for
allocation hunts). Its output is `.diagsession`, which **only Visual Studio can open** — good
for the maintainer, opaque to an agent. `wpr.exe` ships with Windows and needs no install, but
its `.etl` still needs WPA or PerfView to read.

**How to prepare the load, or the profile is unreadable:** one node, fixed history count,
**one thread**, and **not while the machine is shared** — a corpus run in the background turns
the profile into a picture of core contention. Take two nodes, not one: a 30–100 keV node
(three quarters of all histories, one interaction each) and 2614 keV (secondaries, pair
production). Their profiles should look different; if they don't, that is itself a finding.

`CorpusMatrixProbe` has the keys for exactly this load (added 17.08.2026, `T43`):
`--nodes=2 --emin=30 --emax=31 --threads=1 --n=400000 --target=0 --force` gives one
low-energy node on one thread in about ten seconds — enough samples, short enough to iterate.
`--dump=<file.csv>` writes the per-node breakdown (histories, achieved noise, seconds), which is
what tells you *where* the time is before you profile *why*.

⚠ **A short run inflates one-time costs.** In the ten-second load above
`MaterialDatabase.Load()` shows 4.7 % — that is the database read at startup, not per-history
work, and on a real node it is invisible. Read the flat profile with that in mind.
