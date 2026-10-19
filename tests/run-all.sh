#!/usr/bin/env bash
# Corre TODOS los arneses de tests/ (uno por uno) y dice cuáles fallaron. Ver tests/README.md.
#   tests/run-all.sh                       # todos
#   tests/run-all.sh dailytest votetest    # solo esos
# Necesita una base LOCAL con la instalación completa y su .env (o Postgres__ConnectionString). Los resultados quedan en tests/.logs/<arnés>.log.
set -u
root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
logs="$root/.logs"; mkdir -p "$logs"

echo "Compilando el bot..."
dotnet build "$root/../bot_ds_rpg.csproj" --nologo -v q >/dev/null || { echo "No compila el bot: arreglalo antes de correr los arneses."; exit 1; }

if [ $# -gt 0 ]; then names=("$@"); else
  names=(); for d in "$root"/*/; do n="$(basename "$d")"; [ -f "$d/$n.csproj" ] && names+=("$n"); done
  IFS=$'\n' names=($(printf '%s\n' "${names[@]}" | sort -V)); unset IFS
fi

bad=()
for n in "${names[@]}"; do
  log="$logs/$n.log"
  dotnet run --project "$root/$n/$n.csproj" > "$log" 2>&1; code=$?
  fails=$(grep -c "\[FALLA\]" "$log")
  if [ $code -eq 0 ] && [ "$fails" -eq 0 ]; then echo "OK     $n"; else echo "FALLO  $n  (salida $code, $fails en [FALLA]; ver tests/.logs/$n.log)"; bad+=("$n"); fi
done

echo
if [ ${#bad[@]} -eq 0 ]; then echo "Todos los arneses pasaron (${#names[@]})."; exit 0; fi
echo "Fallaron ${#bad[@]}: ${bad[*]}"; exit 1
