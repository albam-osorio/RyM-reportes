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
  },
  "Email": {
    "SmtpHost": "smtp.gmail.com",
    "SmtpPort": 587,
    "UseStartTls": true,
    "Username": "rym.application@gmail.com",
    "Password": "",
    "From": "rym.application@gmail.com"
  }
}
```

Configura la cadena real como variable de entorno del sistema, fuera de git:

```powershell
[Environment]::SetEnvironmentVariable("Database__ConnectionString", "<cadena-de-conexion-sql-server>", "Machine")
```

Cuando este disponible la app password de Gmail, configura tambien:

```powershell
[Environment]::SetEnvironmentVariable("Email__SmtpHost", "smtp.gmail.com", "Machine")
[Environment]::SetEnvironmentVariable("Email__SmtpPort", "587", "Machine")
[Environment]::SetEnvironmentVariable("Email__UseStartTls", "true", "Machine")
[Environment]::SetEnvironmentVariable("Email__Username", "rym.application@gmail.com", "Machine")
[Environment]::SetEnvironmentVariable("Email__Password", "<app-password-de-gmail>", "Machine")
[Environment]::SetEnvironmentVariable("Email__From", "rym.application@gmail.com", "Machine")
```

Despues de cambiar variables de entorno, reinicia el servicio si ya estaba instalado.

## 3. Instalar servicio

Abre PowerShell como administrador desde la carpeta donde descomprimiste el paquete y ejecuta:

```powershell
.\scripts\install-service.ps1 -PublishPath C:\Services\RymReportes -Port 5085
```

## 4. Crear o rescatar administrador

Desde `C:\Services\RymReportes`:

```powershell
.\RymReportes.Web.exe admin create --email admin@empresa.com --name "Administrador"
```

Si un administrador queda bloqueado o sin acceso al correo:

```powershell
.\RymReportes.Web.exe admin reset-password --email admin@empresa.com
```

El comando imprime una contrasena temporal y obliga el cambio al entrar.

## 5. Probar

Desde el servidor:

```powershell
Invoke-RestMethod http://localhost:5085/health
```

Desde otro equipo con acceso de red:

```text
http://NOMBRE-SERVIDOR:5085
```

## 6. Desinstalar

```powershell
.\scripts\uninstall-service.ps1
```
