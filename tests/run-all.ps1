# Corre TODOS los arneses de tests/ (uno por uno, en orden) y dice cuáles fallaron. Ver tests/README.md.
#
#   .\tests\run-all.ps1                       # todos
#   .\tests\run-all.ps1 -Only dailytest,votetest   # solo esos
#
# Necesita una base de datos LOCAL con la instalación completa (Database/run_fresh_install.sql) y su .env (o la variable Postgres__ConnectionString). Cada arnés termina con "TODO OK" o con
# "N FALLAS" y devuelve el código de salida; acá se cuenta además cualquier línea "[FALLA]". Los resultados completos quedan en tests/.logs/<arnés>.log (no se sube al repo).
param(
    [string[]]$Only = @()
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSCommandPath
$logs = Join-Path $root ".logs"
New-Item -ItemType Directory -Force $logs | Out-Null

$projects = Get-ChildItem $root -Directory |
    Where-Object { $_.Name -ne ".logs" -and $_.Name -ne "Shared" -and (Test-Path (Join-Path $_.FullName "$($_.Name).csproj")) } |
    Sort-Object { [regex]::Replace($_.Name, '\d+', { param($m) $m.Value.PadLeft(6, '0') }) }
if ($Only.Count -gt 0) { $projects = $projects | Where-Object { $Only -contains $_.Name } }

Write-Host "Compilando el bot..." -ForegroundColor Cyan
dotnet build (Join-Path $root "..\bot_ds_rpg.csproj") --nologo -v q | Out-Null
if ($LASTEXITCODE -ne 0) { throw "No compila el bot: arreglalo antes de correr los arneses." }

$bad = @()
foreach ($p in $projects) {
    $log = Join-Path $logs "$($p.Name).log"
    dotnet run --project (Join-Path $p.FullName "$($p.Name).csproj") *> $log
    $exit = $LASTEXITCODE
    $falla = (Select-String -Path $log -Pattern "\[FALLA\]" -SimpleMatch:$false | Measure-Object).Count
    if ($exit -eq 0 -and $falla -eq 0) {
        Write-Host ("OK     " + $p.Name) -ForegroundColor Green
    }
    else {
        Write-Host ("FALLO  " + $p.Name + "  (salida $exit, $falla chequeos en [FALLA]; ver tests/.logs/$($p.Name).log)") -ForegroundColor Red
        $bad += $p.Name
    }
}

Write-Host ""
if ($bad.Count -eq 0) { Write-Host "Todos los arneses pasaron ($($projects.Count))." -ForegroundColor Green; exit 0 }
Write-Host ("Fallaron " + $bad.Count + ": " + ($bad -join ", ")) -ForegroundColor Red
exit 1
