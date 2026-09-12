# П23 12.09.2026 — ПОБИТОВЫЙ КОНТРОЛЬ: ключи ВЫКЛ обязаны дать матрицу, равную эталону
# HEAD (fb86bb45, worktree C:\Users\moroz\bqp23, build_p23ref) до бита — MatrixDiffProbe
# 0.000 % по всем узлам И равный отпечаток тела (хвост BODY). Рецепт A101/A104: 6 узлов,
# 20 тыс. историй, плоский счёт (--target=0), ОДИН поток (на нескольких поток
# недетерминирован, A104). Сцены — копии в подкаталогах ref / new_off / new_on
# (проба ПИШЕТ .rmx в --dir; склад не трогается).
# Третье плечо new_on — все три ключа ВКЛ (--lbin=1 --pkch=1 --lys=1): клеймо обязано
# ОТЛИЧАТЬСЯ, а разность против new_off — мера самих ключей.
$ErrorActionPreference = 'Continue'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$new = "$root\tools\effmaker\probes\build_p23"
$ref = 'C:\Users\moroz\bqp23\tools\effmaker\probes\build_p23ref'
$out = "$root\handover\p23-physics17-keys\bitwise"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
foreach ($arm in 'ref', 'new_off', 'new_on') {
    New-Item -ItemType Directory -Force "$out\$arm" | Out-Null
    foreach ($scene in 'ASN16_point0', 'AS80_point0') {
        Copy-Item "$root\handover\p23-physics17-keys\scenes\$scene.in" "$out\$arm\$scene.in" -Force
        Remove-Item "$out\$arm\$scene.rmx" -ErrorAction SilentlyContinue
    }
}
$common = @('--nodes=6', '--n=20000', '--target=0', '--threads=1', '--force')
Push-Location $ref
& "$ref\CorpusMatrixProbe.exe" "--dir=$out\ref" @common > "$out\ref.txt" 2>&1
"ref code=$LASTEXITCODE" | Out-File -Append "$out\codes.txt"
Pop-Location
Push-Location $new
& "$new\CorpusMatrixProbe.exe" "--dir=$out\new_off" @common > "$out\new_off.txt" 2>&1
"new_off code=$LASTEXITCODE" | Out-File -Append "$out\codes.txt"
& "$new\CorpusMatrixProbe.exe" "--dir=$out\new_on" @common --lbin=1 --pkch=1 --lys=1 > "$out\new_on.txt" 2>&1
"new_on code=$LASTEXITCODE" | Out-File -Append "$out\codes.txt"
foreach ($scene in 'ASN16_point0', 'AS80_point0') {
    & "$new\MatrixDiffProbe.exe" "--a=$out\ref\$scene.rmx" "--b=$out\new_off\$scene.rmx" > "$out\diff_off_$scene.txt" 2>&1
    "diff_off $scene code=$LASTEXITCODE" | Out-File -Append "$out\codes.txt"
    & "$new\MatrixDiffProbe.exe" "--a=$out\new_off\$scene.rmx" "--b=$out\new_on\$scene.rmx" > "$out\diff_on_$scene.txt" 2>&1
    "diff_on $scene code=$LASTEXITCODE" | Out-File -Append "$out\codes.txt"
}
Pop-Location
"done $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes.txt"
