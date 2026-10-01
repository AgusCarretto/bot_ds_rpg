# Asado y Acero RPG — imagen para correr el bot en cualquier host con Docker (un VPS, Railway, Fly, Koyeb, etc.).
# El bot es un proceso que se conecta a Discord (no escucha ningún puerto): no hace falta exponer nada.
#
# IMPORTANTE: se usan las imágenes estándar (Debian), NO las "chiseled" ni las Alpine: el bot usa
# string.Normalize(FormD) para los autocompletados sin tildes, y eso necesita ICU, que esas imágenes no traen.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Primero solo el proyecto: la restauración de paquetes queda en caché y no se repite si solo cambió el código.
COPY bot_ds_rpg.csproj ./
RUN dotnet restore

COPY . .
RUN dotnet publish bot_ds_rpg.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Menos memoria a cambio de un poquito de CPU: el bot casi no usa CPU y en un host barato lo que cuenta es la RAM.
ENV DOTNET_gcServer=0 \
    DOTNET_GCConserveMemory=5

# La configuración (token, base, servidor) viene SIEMPRE por variables de entorno — ver DEPLOY.md. El .env local
# no entra a la imagen (.dockerignore).
ENTRYPOINT ["dotnet", "bot_ds_rpg.dll"]
