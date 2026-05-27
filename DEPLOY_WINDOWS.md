# Despliegue Windows Server

## 1. Copiar paquete

Descomprime `RymReportes-win-x64.zip` en el servidor.

Estructura esperada:

```text
C:\Services\RymReportes\
  RymReportes.Web.exe
  appsettings.Production.json
```

## 2. Revisar configuracion

En `C:\Services\RymReportes\appsettings.Production.json` debe quedar:

```json
{
  "Urls": "http://0.0.0.0:5085",
  "Database": {
    "ConnectionString": ""
  }
}
```

Configura la cadena real como variable de entorno del sistema, fuera de git:

```powershell
[Environment]::SetEnvironmentVariable("Database__ConnectionString", "<cadena-de-conexion-sql-server>", "Machine")
```

Despues de cambiar variables de entorno, reinicia el servicio si ya estaba instalado.

## 3. Instalar servicio

Abre PowerShell como administrador desde la carpeta donde descomprimiste el paquete y ejecuta:

```powershell
.\scripts\install-service.ps1 -PublishPath C:\Services\RymReportes -Port 5085
```

## 4. Probar

Desde el servidor:

```powershell
Invoke-RestMethod http://localhost:5085/health
```

Desde otro equipo con acceso de red:

```text
http://NOMBRE-SERVIDOR:5085
```

## 5. Desinstalar

```powershell
.\scripts\uninstall-service.ps1
```
