-- =========================================================
-- Migración: familia de arma para la sinergia clase-arma en combate.
-- Ejecutar UNA SOLA VEZ contra la base ya creada.
-- =========================================================
-- Los valores deben coincidir exactamente con ClassCatalog.WeaponType en el código:
-- Guerrero -> 'Espadas', Ninja -> 'Dagas', Arquero -> 'Arcos', Hechicero -> 'Grimorios'.

ALTER TABLE items ADD COLUMN IF NOT EXISTS weapon_family TEXT
    CHECK (weapon_family IN ('Espadas', 'Dagas', 'Arcos', 'Grimorios'));

-- Clasificación de las armas ya cargadas. "Hacha de Hierro MK3" queda en 'Espadas' (arma
-- cuerpo a cuerpo pesada, mismo espíritu que Guerrero) — si preferís separarla en su propia
-- familia sin sinergia para nadie, corré un UPDATE puntual dejándola en NULL.
UPDATE items SET weapon_family = 'Espadas' WHERE name IN ('Espada de Madera', 'Hacha de Hierro MK3', 'Hoja de Acero Puro');

-- Armas nuevas para que Ninja, Arquero y Hechicero también puedan activar la sinergia de clase
-- (hasta ahora solo existían Espadas). Mismo patrón de progresión que las Espadas existentes:
-- Común (stat 5) -> Raro (stat 15) -> Épico (stat 35), buy_price = 30% de margen sobre sell_price.
INSERT INTO items (name, type, rarity, stat_value, sell_price, buy_price, weapon_family) VALUES
-- 🗡️ Dagas (Ninja)
('Daga Oxidada',            'Weapon', 'Común', 5,  20,  26,  'Dagas'),
('Dagas Gemelas de Sombra', 'Weapon', 'Raro',  15, 150, 195, 'Dagas'),
('Colmillo Nocturno',       'Weapon', 'Épico', 35, 500, 650, 'Dagas'),

-- 🏹 Arcos (Arquero)
('Arco Corto de Sauce',     'Weapon', 'Común', 5,  20,  26,  'Arcos'),
('Arco Largo del Cazador',  'Weapon', 'Raro',  15, 150, 195, 'Arcos'),
('Arco Élfico Ancestral',   'Weapon', 'Épico', 35, 500, 650, 'Arcos'),

-- 📖 Grimorios (Hechicero) — incluye un báculo, misma familia que los grimorios para la sinergia
('Grimorio Desgastado',       'Weapon', 'Común', 5,  20,  26,  'Grimorios'),
('Báculo del Aprendiz',       'Weapon', 'Raro',  15, 150, 195, 'Grimorios'),
('Grimorio de las Tormentas', 'Weapon', 'Épico', 35, 500, 650, 'Grimorios')
ON CONFLICT DO NOTHING;
