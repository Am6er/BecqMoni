# Отсоединённый запуск долгого счёта (ночной склад матриц, корпус) — ВНЕ дерева процессов
# и ВНЕ Job Object сессии Claude (`T262`, П116 21.09.2026).
#
#   & 'tools\CORPUS\scripts\detached_run.ps1' -Cmd D:\BqMoni_Claude\p114\store_run.cmd
#   & 'tools\CORPUS\scripts\detached_run.ps1' -SelfTest            # положительный контроль, ~40 с
#   & 'tools\CORPUS\scripts\detached_run.ps1' -Decode 1073807364   # что значит код в *_done.txt
#
# ⛔ Звать оператором вызова `&`, не `pwsh -File` (`T84`/`T91`) — правило одно на всю оснастку.
#
# ═══ Что это лечит и чего НЕ лечит — всё измерено 21.09.2026 (журнал П116) ═════════════
#
# Механизм: `Invoke-CimMethod Win32_Process.Create` — процесс рождает служба WMI (`WmiPrvSE`),
# а не канал `pwsh`: родитель — `WmiPrvSE.exe`, НИ В ОДНОМ Job Object (`IsProcessInJob` = False),
# тот же интерактивный сеанс, окружение ПОЛЬЗОВАТЕЛЯ (`APPDATA`, `USERPROFILE`, `PATH` — те же
# 45 переменных, что у задачи планировщика), окно скрыто (`Win32_ProcessStartup.ShowWindow=0`).
#
#   * канал `pwsh` сессии (и всё, что из него запущено обычным `Start-Process`) сидит в Job Object
#     ПАКЕТА Claude Desktop (MSIX): в одном job — все `claude.exe`, MCP-серверы, `pwsh`, `conhost`;
#     пределы `0x800` = BREAKAWAY_OK, KILL_ON_JOB_CLOSE НЕТ. `Start-Process` кладёт ребёнка ещё и во
#     вложенный job (флаги 0). `TerminateJobObject` по job'у родителя (опыт E3 — так гибнет пакет:
#     «Снять задачу» диспетчера по Claude, принудительное обновление пакета) убивает ВСЮ нагрузку
#     разом, `*_done.txt` не пишется. Процесс от WMI в этом job'е не состоит — переживает;
#   * ОБРЫВ КАНАЛА `Start-Process`-ребёнок ПЕРЕЖИВАЕТ И БЕЗ ЭТОГО СКРИПТА: `Stop-Process` родителя
#     (E1), перезапуск MCP-сервера хостом вместе с его `pwsh` (E2b, `pwsh_close`) — нагрузка жива.
#     Посылка строки `T262` («гибнет вместе с сессией распорядителя») НЕ ПОДТВЕРДИЛАСЬ;
#   * ⛔ ЧЕГО НЕ ЛЕЧИТ НИЧТО: счёт П114 19.09.2026 19:42:11 убило ВЫКЛЮЧЕНИЕ КОМПЬЮТЕРА —
#     журнал System: `User32 1074` 19:42:10 «StartMenuExperienceHost … "Выключение питания" … от имени
#     amber-msi\amber», `Winlogon 7002` 19:42:13; код `exit 1073807364` = 0x40010004 =
#     DBG_TERMINATE_PROCESS (это НЕ STATUS_CONTROL_C_EXIT, тот 0xC000013A). Выход из системы —
#     то же: процессы сеанса пользователя снимаются все. Пережить выход мог бы только запуск
#     планировщиком «вне зависимости от входа» (S4U), но регистрировать такую задачу без повышения
#     Windows не даёт (`schtasks /ru <user> /np` → «Введите пароль… Отказано в доступе»), а пароль
#     и повышение полосе запрещены. Значит: пока идёт ночной счёт — компьютер не выключать и из
#     системы не выходить; сон допустим (процесс продолжится после пробуждения).
#
# Почему WMI, а не планировщик (`schtasks`, тоже работает — опыт E4): задача Interactive без `/ru`
# регистрируется и снимается без повышения, процесс от `svchost` Schedule и вне job'а пакета,
# снятие задачи ПОСЛЕ старта процесс не убивает; НО (1) окно консоли не скрыть — вылезает Windows
# Terminal (`PseudoConsoleWindow`), (2) остаётся объект задачи, (3) вход S4U всё равно недоступен.
# Планировщик под повышением (`BqPerfViewStart` в CLAUDE.md «Profiling») — ДРУГОЙ разряд: там
# фиксированная команда в каталоге администратора, потому что задача ПОВЫШЕННАЯ. Здесь повышения
# нет, привилегий не добавляется, командная строка — файл полосы.
#
# ⚠ Окружение процесса — ПОЛЬЗОВАТЕЛЬСКОЕ, а не канала: `$env:X = …`, выставленное в `pwsh` перед
#   вызовом, до `.cmd` НЕ доедет. Всё нужное — в самом `.cmd` (полные пути, `cd /d`, как у
#   `make_cmd.py` П114: ASCII, CRLF — грабли П87/П99).
#
# Что печатает и пишет: PID, родитель, сеанс, время старта; то же — в файл `<cmd>.launch.txt` рядом с
# `.cmd`, чтобы `status.py` полосы читал PID и родителя и обрыв был виден по логу (п. 3 `T262`).
# Проверить, что бежит: `Get-Process -Id <PID>`; дерево — `Get-CimInstance Win32_Process -Filter
# "ParentProcessId=<PID>"`.
#
# Коды возврата: 0 — запущено (или самопроверка прошла); 2 — нет `.cmd`; 3 — WMI отказал (код в
# печати); 4 — самопроверка НЕ прошла; 5 — неверные ключи.
[CmdletBinding(PositionalBinding = $false)]
param(
    [string]$Cmd = '',
    [string]$WorkDir = '',
    [switch]$SelfTest,
    [string]$Scratch = '',
    [Nullable[int64]]$Decode = $null
)
$ErrorActionPreference = 'Stop'

# ── расшифровка кода из `echo exit %errorlevel%` — отпечатки измерены 21.09.2026 (П116, F1/F2, П114) ──
$script:ExitCodes = @(
    @{ Code = -1;          Hex = '0xFFFFFFFF'; Text = 'TerminateProcess(-1): `Stop-Process`, диспетчер задач (кнопка «Завершить процесс» у процесса)' },
    @{ Code = 1;           Hex = '0x00000001'; Text = '`taskkill /F` — либо СВОЙ код 1 самой пробы (отказ); различать по её логу' },
    @{ Code = 1073807364;  Hex = '0x40010004'; Text = 'DBG_TERMINATE_PROCESS — выключение/перезагрузка компьютера или выход пользователя из системы (П114 19.09.2026 19:42:11 = событие System 1074 «Выключение питания»); НЕ Ctrl+C' },
    @{ Code = -1073741510; Hex = '0xC000013A'; Text = 'STATUS_CONTROL_C_EXIT — Ctrl+C/Ctrl+Break или закрытие окна консоли' },
    @{ Code = 0;           Hex = '0x00000000'; Text = 'штатное завершение' }
)

function Decode-ExitCode([int64]$code) {
    $u = if ($code -lt 0) { [uint32]($code + 4294967296) } else { [uint32]$code }
    $row = $script:ExitCodes | Where-Object { [uint32]($_.Code -lt 0 ? $_.Code + 4294967296 : $_.Code) -eq $u }
    if ($row) { 'exit {0} (0x{1:X8}): {2}' -f $code, $u, $row.Text } else { 'exit {0} (0x{1:X8}): код неизвестен — смотреть лог пробы' -f $code, $u }
}

function Start-Detached([string]$cmdPath, [string]$workDir) {
    $full = [IO.Path]::GetFullPath($cmdPath)
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { Write-Host "НЕТ ФАЙЛА: $full" -ForegroundColor Red; return @{ Code = 2 } }
    if ($full -match '\s') { Write-Host "⚠ в пути есть пробел — cmd /c может разрезать его; полосы держат .cmd в D:\BqMoni_Claude\<полоса>\ без пробелов" -ForegroundColor Yellow }
    if (-not $workDir) { $workDir = Split-Path -Parent $full }
    $startup = New-CimInstance -ClassName Win32_ProcessStartup -ClientOnly -Property @{ ShowWindow = [uint16]0 }
    $r = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{
        CommandLine               = 'cmd.exe /c "' + $full + '"'
        CurrentDirectory          = $workDir
        ProcessStartupInformation = $startup
    }
    if ($r.ReturnValue -ne 0) { Write-Host ('WMI Win32_Process.Create отказал: код {0} (2 — доступ, 3 — привилегия, 8 — неизвестный сбой, 9 — путь, 21 — параметр)' -f $r.ReturnValue) -ForegroundColor Red; return @{ Code = 3 } }
    Start-Sleep -Milliseconds 700
    $w = Get-CimInstance Win32_Process -Filter "ProcessId=$($r.ProcessId)"
    $parentName = if ($w) { (Get-Process -Id $w.ParentProcessId -ErrorAction SilentlyContinue).ProcessName } else { '?' }
    $lines = @(
        ('запущено {0:yyyy-MM-dd HH:mm:ss}: {1}' -f (Get-Date), $full),
        ('PID {0}; родитель {1} ({2}); сеанс {3}; рабочий каталог {4}' -f $r.ProcessId, ($w ? $w.ParentProcessId : '?'), $parentName, ($w ? $w.SessionId : '?'), $workDir),
        ('проверить: Get-Process -Id {0}   |   дерево: Get-CimInstance Win32_Process -Filter "ParentProcessId={0}"' -f $r.ProcessId),
        'вне Job Object сессии Claude; переживает обрыв канала и снятие пакета Claude; НЕ переживает выключение/выход из системы (T262)'
    )
    $lines | ForEach-Object { Write-Host $_ }
    try { [IO.File]::WriteAllLines($full + '.launch.txt', $lines, (New-Object Text.UTF8Encoding $false)) } catch { Write-Host "⚠ .launch.txt не записан: $($_.Exception.Message)" -ForegroundColor Yellow }
    return @{ Code = 0; Pid = [int]$r.ProcessId; Parent = ($w ? [int]$w.ParentProcessId : 0); ParentName = $parentName }
}

# ── самопроверка: убийство родителя И его Job Object; отсоединённый — жив, старый способ — мёртв ──
function Invoke-SelfTest([string]$scratch) {
    if (-not $scratch) { $scratch = (Test-Path 'D:\BqMoni_Claude') ? 'D:\BqMoni_Claude\detached_selftest' : (Join-Path $env:TEMP 'detached_selftest') }
    New-Item -ItemType Directory -Force -Path $scratch | Out-Null
    Add-Type -Namespace DetachedRun -Name JobCtl -MemberDefinition @'
[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] public static extern IntPtr CreateJobObject(IntPtr attrs, string name);
[DllImport("kernel32.dll", SetLastError = true)] public static extern bool SetInformationJobObject(IntPtr hJob, int infoClass, byte[] info, int len);
[DllImport("kernel32.dll", SetLastError = true)] public static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);
[DllImport("kernel32.dll", SetLastError = true)] public static extern bool TerminateJobObject(IntPtr hJob, uint exitCode);
[DllImport("kernel32.dll", SetLastError = true)] public static extern bool CloseHandle(IntPtr h);
[DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
'@
    $self = $PSCommandPath
    $ok = $true
    $results = @{}
    foreach ($way in 'detached', 'oldway') {
        $tag = $way
        $load = Join-Path $scratch "load_$tag.cmd"
        $done = Join-Path $scratch "${tag}_done.txt"
        $pidFile = Join-Path $scratch "${tag}_pid.txt"
        Remove-Item -LiteralPath $done, $pidFile, ($load + '.launch.txt') -ErrorAction SilentlyContinue
        # нагрузка: 60 с ping, потом строка exit — как store_run.cmd П114 (ASCII, CRLF)
        $body = "@echo off`r`nping -n 61 127.0.0.1 > nul`r`necho exit %errorlevel% %date% %time% > $done`r`n"
        [IO.File]::WriteAllText($load, $body, [Text.Encoding]::ASCII)
        # «родитель» — отдельный pwsh -NoProfile (не канал сессии); 3 с ждёт, чтобы попасть в наш job ДО запуска нагрузки
        if ($way -eq 'detached') {
            $inner = "Start-Sleep 3; `$r = & '$self' -Cmd '$load' *>&1 | Out-String; ([regex]::Match(`$r, 'PID (\d+)')).Groups[1].Value | Set-Content -LiteralPath '$pidFile'; Start-Sleep 600"
        } else {
            $inner = "Start-Sleep 3; (Start-Process cmd.exe -ArgumentList '/c $load' -WindowStyle Hidden -PassThru).Id | Set-Content -LiteralPath '$pidFile'; Start-Sleep 600"
        }
        $parent = Start-Process pwsh -ArgumentList '-NoProfile', '-Command', $inner -WindowStyle Hidden -PassThru
        # job с KILL_ON_JOB_CLOSE|BREAKAWAY_OK (0x2800) — эмуляция job'а пакета Claude Desktop
        $job = [DetachedRun.JobCtl]::CreateJobObject([IntPtr]::Zero, $null)
        $buf = New-Object byte[] 144; [BitConverter]::GetBytes([uint32]0x2800).CopyTo($buf, 16)
        if (-not [DetachedRun.JobCtl]::SetInformationJobObject($job, 9, $buf, 144)) { throw 'SetInformationJobObject' }
        $hp = [DetachedRun.JobCtl]::OpenProcess(0x101, $false, $parent.Id)
        if (-not [DetachedRun.JobCtl]::AssignProcessToJobObject($job, $hp)) { throw ('AssignProcessToJobObject: ' + [Runtime.InteropServices.Marshal]::GetLastWin32Error()) }
        [void][DetachedRun.JobCtl]::CloseHandle($hp)
        $deadline = (Get-Date).AddSeconds(25); $loadPid = 0
        while ((Get-Date) -lt $deadline) { if (Test-Path -LiteralPath $pidFile) { $t = (Get-Content -LiteralPath $pidFile -Raw).Trim(); if ($t -match '^\d+$') { $loadPid = [int]$t; break } }; Start-Sleep -Milliseconds 500 }
        if (-not $loadPid) { Write-Host "[$way] нагрузка не запустилась за 25 с — самопроверка не состоялась" -ForegroundColor Red; $ok = $false; Stop-Process -Id $parent.Id -Force -ErrorAction SilentlyContinue; continue }
        Start-Sleep 1
        $before = Get-CimInstance Win32_Process -Filter "ProcessId=$loadPid"
        $pingBefore = (Get-CimInstance Win32_Process -Filter "ParentProcessId=$loadPid AND Name='PING.EXE'").ProcessId
        Write-Host ('[{0}] ДО: родитель pwsh {1} в job 0x{2:x}; нагрузка cmd PID {3} (родитель {4} {5}), ping PID {6}' -f $way, $parent.Id, $job.ToInt64(), $loadPid, $before.ParentProcessId, (Get-Process -Id $before.ParentProcessId -ErrorAction SilentlyContinue).ProcessName, ($pingBefore ?? '—'))
        # убийство: сперва родитель, потом весь его job
        Stop-Process -Id $parent.Id -Force
        Start-Sleep 1
        $afterStop = [bool](Get-Process -Id $loadPid -ErrorAction SilentlyContinue)
        [void][DetachedRun.JobCtl]::TerminateJobObject($job, 77)
        Start-Sleep 3
        $after = Get-CimInstance Win32_Process -Filter "ProcessId=$loadPid"
        $alive = [bool]$after -and ($after.CreationDate -eq $before.CreationDate)
        $pingAlive = [bool]($pingBefore -and (Get-Process -Id $pingBefore -ErrorAction SilentlyContinue))
        [void][DetachedRun.JobCtl]::CloseHandle($job)
        Write-Host ('[{0}] ПОСЛЕ Stop-Process родителя: cmd жив={1}; ПОСЛЕ TerminateJobObject: cmd PID {2} жив={3}, ping PID {4} жив={5}' -f $way, $afterStop, $loadPid, $alive, ($pingBefore ?? '—'), $pingAlive)
        $results[$way] = $alive
        # уборка: свою нагрузку снять
        if ($pingBefore) { Stop-Process -Id $pingBefore -Force -ErrorAction SilentlyContinue }
        Stop-Process -Id $loadPid -Force -ErrorAction SilentlyContinue
    }
    if ($results.Count -eq 2) {
        if ($results['detached'] -and -not $results['oldway']) {
            Write-Host 'САМОПРОВЕРКА ПРОШЛА: отсоединённый запуск пережил убийство родителя и его Job Object; старый Start-Process — погиб (положительный контроль убийцы)' -ForegroundColor Green
        } else {
            $ok = $false
            Write-Host ('САМОПРОВЕРКА НЕ ПРОШЛА: detached жив={0} (ждали True), oldway жив={1} (ждали False — иначе убийца не измерен)' -f $results['detached'], $results['oldway']) -ForegroundColor Red
        }
    } else { $ok = $false }
    Start-Sleep 1
    Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
    return $ok
}

if ($null -ne $Decode) { Decode-ExitCode $Decode; exit 0 }
if ($SelfTest) { exit ((Invoke-SelfTest $Scratch) ? 0 : 4) }
if (-not $Cmd) { Write-Host 'нужен -Cmd <путь .cmd>, или -SelfTest, или -Decode <код>' -ForegroundColor Red; exit 5 }
$res = Start-Detached $Cmd $WorkDir
exit $res.Code
