# Banco, penalidad por muerte, desmantelar → Polvo, encantamientos y logros nuevos (v0.9.0)

**Pedido del dueño (2026-10-05, aprobado con "dale"):**
1. **Banco**: hay que comprar una cuenta (1.000 de oro). Después se deposita y se retira oro; lo del banco no se toca.
2. **Penalidad por muerte**: estés en el nivel que estés, al perder un combate la EXP del nivel vuelve a 0 y se pierde el 5 % del oro (el de la billetera, no el del banco). No baja de nivel.
3. **Desmantelar materiales → Polvo** (1–5 por comando). Craftear "se olvida". El Polvo + oro alimenta los **encantamientos** (con tiers).
4. **Logros nuevos**: cantidad de comandos, enemigos derrotados y más; con páginas.
Relación y mascotas quedan para más adelante (ver MEJORAS.md).

## Decisiones (todas tunables en UN archivo de GameData)
- `GameData/BankRules.cs`: precio de la cuenta 1.000. Sin interés ni tope por ahora (el valor del banco es estar a salvo de la penalidad).
- `GameData/DeathPenalty.cs`: 5 % del oro de la billetera (redondeo hacia abajo) y EXP del nivel a 0. Aplica a toda derrota: `/hunt`, `/travel`, `/boss`, `/autohunt`, `/use` en pelea y raid que cae entero. NO aplica a huir, al tiempo agotado, ni a duelos/arena.
- `GameData/Dismantling.cs`: Polvo por unidad según la rareza (Común 1, Raro 6, Épico 24, Legendario 70, Mítico 500: ~0,5 Polvo por minuto de farmeo, igual para todas), solo Madera, Mineral y Material (drops/trofeos), máximo 5 por comando.
- `GameData/Enchantments.cs`: 5 tiers por pieza (Tibio +4 %, Al Rojo +8 %, Ardiente +13 %, Incandescente +19 %, Soberano +26 %) sobre el stat del ARMA (ATQ, ya con la sinergia de clase) o del AMULETO (DEF).
  Cada intento cuesta oro + Polvo según la zona de la pieza, sortea un tier (40/30/18/9/3) y SOLO reemplaza si sale mejor (nunca baja). Vender la pieza equipada borra su encantamiento.
- Logros nuevos (solo oro y XP, sin cajas: las cajas valen mucho): Comandante (`command_used`), Exterminador (`enemy_defeated`), Cabeza dura (`fight_lost`), Desarmador, Encantador, Ahorrista, Duelista, Campeón de la Arena, Apostador, Mercader y Ascenso. `/achievements` con páginas por categoría.

## Tareas
1. Reglas puras + arnés (`v90test`).
2. Esquema (`users`: `has_bank`, `bank_gold`, `dust`, `weapon_enchant`, `amulet_enchant`) + modelo + repositorios (banco, polvo/encantar, penalidad) con guardas atómicas.
3. Comandos: `/bank`, `/dismantle`, `/enchant` (+ `aa bank|dismantle|enchant`), autocompletar de desmantelar, `/help`.
4. Penalidad en los cuatro lugares donde se pierde y en los mensajes de derrota.
5. Perfil (banco, polvo, encantamiento), perfil de combate con encantamiento, vender pieza borra el encantamiento.
6. Logros: eventos nuevos (`command_used`, `enemy_defeated`, ...), catálogo, páginas.
7. Verificación (regresión + instalación limpia vs base real), docs, versión 0.9.0, publicación.
