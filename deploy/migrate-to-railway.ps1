<#
  Copia la base REAL (la del .env de tu PC: jugadores, inventarios, historial, todo) a la base de Postgres de Railway.

  Cuándo usarlo: UNA vez, después de crear el servicio Postgres en Railway y ANTES de arrancar el bot allá. Si preferís empezar de cero en
  Railway (sin los datos de tus amigos) no lo necesitás: DEPLOY.md explica cómo cargar la base vacía con run_fresh_install.sql.

  Uso (PowerShell, desde la raíz del repo):
      .\deploy\migrate-to-railway.ps1 -TargetUrl "postgresql://postgres:CLAVE@HOST.proxy.rlwy.net:PUERTO/railway"

  -TargetUrl es el valor de DATABASE_PUBLIC_URL del servicio Postgres de Railway (Variables). Para que exista hay que activar
  "Public Networking" en Settings > Networking de ese servicio; cuando termines, desactivalo (el tráfico por ahí se cobra y queda expuesto).

  Seguridad: nunca toca la base de tu PC (solo la lee), y se niega a copiar sobre una base de destino que ya tenga tablas
  (usá -Replace solo si querés pisarla a propósito).
#>
param(
    [Parameter(Mandatory = $true)][string]$TargetUrl,
    [string]$EnvFile = (Join-Path $PSScriptRoot '..\.env'),
    [string]$PgBin = '',
    [switch]$Replace
)

$ErrorActionPreference = 'Stop'

# --- herramientas de Postgres (pg_dump, pg_restore, psql) ---
function Find-PgTool([string]$name) {
    if ($PgBin -ne '') {
        $p = Join-Path $PgBin "$name.exe"
        if (Test-Path $p) { return $p }
        throw "No encontré $name.exe en $PgBin."
    }
    $cmd = Get-Command $name -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $found = Get-ChildItem "C:\Program Files\PostgreSQL\*\bin\$name.exe" -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
    if ($found) { return $found.FullName }
    throw "No encontré $name.exe. Instalá las herramientas de PostgreSQL o pasá -PgBin 'C:\Program Files\PostgreSQL\18\bin'."
}
$pgDump = Find-PgTool 'pg_dump'
$pgRestore = Find-PgTool 'pg_restore'
$psql = Find-PgTool 'psql'

# --- la base de origen: la cadena de conexión del .env ---
if (-not (Test-Path $EnvFile)) { throw "No encontré el .env en $EnvFile." }
$line = Get-Content $EnvFile | Where-Object { $_ -like 'Postgres__ConnectionString=*' } | Select-Object -First 1
if (-not $line) { throw "El .env no tiene Postgres__ConnectionString." }
$parts = @{}
foreach ($piece in $line.Substring('Postgres__ConnectionString='.Length).Trim().Trim('"').Split(';')) {
    $i = $piece.IndexOf('=')
    if ($i -gt 0) { $parts[$piece.Substring(0, $i).Trim().ToLower()] = $piece.Substring($i + 1).Trim() }
}
$srcHost = $parts['server']; $srcPort = $parts['port']; $srcDb = $parts['database']; $srcUser = $parts['user id']; $srcPass = $parts['password']
if (-not $srcHost -or -not $srcDb -or -not $srcUser) { throw "No pude leer Server / Database / User Id de la cadena de conexión del .env." }
if (-not $srcPort) { $srcPort = '5432' }

if ($TargetUrl -notmatch '^postgres(ql)?://') { throw "-TargetUrl tiene que ser una URL postgresql://usuario:clave@host:puerto/base (DATABASE_PUBLIC_URL de Railway)." }
if ($TargetUrl -match "@$([regex]::Escape($srcHost))[:/]" -and $TargetUrl -match "/$([regex]::Escape($srcDb))(\?|$)") {
    throw "El destino parece ser la MISMA base que el origen ($srcHost / $srcDb). No hago nada."
}

$env:PGCLIENTENCODING = 'UTF8'
$env:PGHOST = $srcHost; $env:PGPORT = $srcPort; $env:PGUSER = $srcUser; $env:PGDATABASE = $srcDb; $env:PGPASSWORD = $srcPass

function Invoke-Scalar([string]$connection, [string]$sql) {
    # $connection vacío = la base de origen (variables PG* de arriba); si no, una URL.
    # (la URL va al final: psql la toma como nombre de base y en Windows las opciones que vengan DESPUÉS de un argumento suelto no se leen)
    if ($connection -eq '') { $out = & $psql -At -c $sql } else { $out = & $psql -At -c $sql $connection }
    if ($LASTEXITCODE -ne 0) { throw "psql falló con: $sql" }
    return ($out | Out-String).Trim()
}

Write-Host "Origen : $srcHost`:$srcPort / $srcDb" -ForegroundColor Cyan
Write-Host 'Destino: (la URL de Railway)' -ForegroundColor Cyan

# --- el destino tiene que estar vacío (salvo -Replace) ---
$tablesAtTarget = [int](Invoke-Scalar $TargetUrl "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public'")
if ($tablesAtTarget -gt 0 -and -not $Replace) {
    throw "La base de destino ya tiene $tablesAtTarget tablas. No la piso. Si querés reemplazarla a propósito, corré de nuevo con -Replace."
}

# --- copia de seguridad del origen (queda en tu PC, es un respaldo más) ---
$dump = Join-Path $env:TEMP ("asado-y-acero-" + (Get-Date -Format 'yyyy-MM-dd_HHmm') + '.dump')
Write-Host "1/3 Sacando la copia de la base real -> $dump" -ForegroundColor Yellow
& $pgDump -Fc --no-owner --no-acl -f $dump
if ($LASTEXITCODE -ne 0) { throw 'pg_dump falló.' }
Write-Host ("    listo ({0:N0} KB)" -f ((Get-Item $dump).Length / 1KB))

# --- restaurar en Railway ---
Write-Host '2/3 Cargando en Railway (puede tardar un minuto)...' -ForegroundColor Yellow
$restoreArgs = @('--no-owner', '--no-acl', '--exit-on-error', '-d', $TargetUrl)
if ($Replace) { $restoreArgs = @('--clean', '--if-exists') + $restoreArgs }
# Primero estricto (frena en el primer error). Si el Postgres de Railway es más viejo que el de tu PC, pg_restore puede quejarse de algún "SET"
# que no conoce (no es un problema de datos): en ese caso se limpia lo que haya quedado y se reintenta tolerante; la verificación de abajo
# es la que dice si los datos quedaron completos.
& $pgRestore @restoreArgs $dump
if ($LASTEXITCODE -ne 0) {
    Write-Host '    pg_restore avisó de errores; verifico qué quedó cargado...' -ForegroundColor DarkYellow
    $tolerant = @('--no-owner', '--no-acl', '--clean', '--if-exists', '-d', $TargetUrl)
    & $pgRestore @tolerant $dump
}

# --- verificación: mismas cuentas en las dos bases ---
Write-Host '3/3 Verificando...' -ForegroundColor Yellow
$tables = @('users', 'items', 'inventory', 'recipes', 'monsters', 'game_events', 'player_stats', 'arena_days', 'mission_claims', 'achievement_claims')
$bad = 0
foreach ($t in $tables) {
    $a = Invoke-Scalar '' "SELECT count(*) FROM $t"
    $b = Invoke-Scalar $TargetUrl "SELECT count(*) FROM $t"
    $ok = ($a -eq $b)
    if (-not $ok) { $bad++ }
    Write-Host ("    {0,-20} origen {1,6}   destino {2,6}   {3}" -f $t, $a, $b, $(if ($ok) { 'OK' } else { 'DISTINTO' })) -ForegroundColor $(if ($ok) { 'Green' } else { 'Red' })
}

if ($bad -eq 0) {
    Write-Host "`nListo: la base de Railway quedó igual a la tuya. Siguiente paso: DEPLOY.md > Railway > crear el servicio del bot." -ForegroundColor Green
    Write-Host 'No te olvides de DESACTIVAR "Public Networking" en el servicio Postgres de Railway.' -ForegroundColor Green
} else {
    Write-Host "`nHay $bad tablas con cuentas distintas: NO arranques el bot en Railway todavía. Mirá el mensaje de pg_restore de arriba." -ForegroundColor Red
    exit 1
}
