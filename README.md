# RYM Reportes

Aplicacion ASP.NET Core .NET 10 para generar reportes de eventos en Excel 365 y descargarlos bajo demanda desde una pagina web.

## Configuracion

La configuracion base esta en `src/RymReportes.Web/appsettings.json`.

- `appsettings.Development.json`: pruebas locales contra `ec2-52-203-6-228.compute-1.amazonaws.com,1433`.
- `appsettings.Production.json`: produccion contra `localhost,1433`.
- La aplicacion escucha por defecto en `http://0.0.0.0:5085` en produccion.

```json
{
  "Urls": "http://0.0.0.0:5085",
  "Database": {
    "ConnectionString": "Server=localhost,1433;Database=rymdb;User Id=remesas;Password=remesas;TrustServerCertificate=True;"
  }
}
```

Para no guardar la cadena de conexion en el archivo, usa user-secrets en desarrollo:

```bash
dotnet user-secrets init --project src/RymReportes.Web
dotnet user-secrets set "Database:ConnectionString" "Server=...;Database=rymdb;User Id=...;Password=...;TrustServerCertificate=True;" --project src/RymReportes.Web
```

## Ejecucion

```bash
dotnet run --project src/RymReportes.Web/RymReportes.Web.csproj --urls http://localhost:5085
```

Abre `http://localhost:5085`.

## Publicacion en Windows Server

Publica la aplicacion en Release:

```powershell
dotnet publish .\src\RymReportes.Web\RymReportes.Web.csproj -c Release -r win-x64 --self-contained false -o C:\Services\RymReportes
```

Instala el servicio desde PowerShell como administrador:

```powershell
.\scripts\windows\install-service.ps1 -PublishPath C:\Services\RymReportes -Port 5085
```

La URL para usuarios sera:

```text
http://NOMBRE-SERVIDOR:5085
```

Para desinstalar:

```powershell
.\scripts\windows\uninstall-service.ps1
```

## Pruebas

```bash
dotnet test
```
