# RYM Reportes

Aplicacion ASP.NET Core .NET 10 para generar reportes de eventos en Excel 365 y descargarlos bajo demanda desde una pagina web.

## Configuracion

La configuracion base esta en `src/RymReportes.Web/appsettings.json`.

- `appsettings.Development.json`: pruebas locales, con cadena real configurada fuera de git.
- `appsettings.Production.json`: produccion contra `localhost,1433`.
- La aplicacion escucha por defecto en `http://0.0.0.0:5085` en produccion.

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

La cadena de conexion real no debe guardarse en git. Configurala con user-secrets en desarrollo:

```bash
dotnet user-secrets init --project src/RymReportes.Web
dotnet user-secrets set "Database:ConnectionString" "<cadena-de-conexion-sql-server>" --project src/RymReportes.Web
```

En Windows Server, usa una variable de entorno del sistema o configura el valor directamente en el archivo publicado que no se sube al repositorio:

```powershell
[Environment]::SetEnvironmentVariable("Database__ConnectionString", "<cadena-de-conexion-sql-server>", "Machine")
```

## Usuarios locales

La aplicacion usa ASP.NET Core Identity con roles `Admin` y `User`.

- Un usuario se registra desde la web y queda pendiente.
- Un administrador aprueba, activa o desactiva usuarios desde `/admin/users.html`.
- Los endpoints de reportes requieren sesion iniciada.
- Los administradores pueden recuperar su contrasena por correo.
- Infraestructura puede rescatar administradores localmente sin depender del correo.

Crear el primer administrador:

```bash
dotnet run --project src/RymReportes.Web/RymReportes.Web.csproj -- admin create --email admin@empresa.com --name "Administrador"
```

Reiniciar la contrasena de un administrador:

```bash
dotnet run --project src/RymReportes.Web/RymReportes.Web.csproj -- admin reset-password --email admin@empresa.com
```

El comando muestra una contrasena temporal y marca el usuario para cambio obligatorio al entrar.

## Gmail SMTP

La cuenta dedicada sera `rym.application@gmail.com`. Cuando este disponible la app password:

1. Entrar a `rym.application@gmail.com`.
2. Abrir `https://myaccount.google.com/security`.
3. Activar `2-Step Verification`.
4. Abrir `https://myaccount.google.com/apppasswords`.
5. Crear una app password llamada `RYM Reportes`.
6. Guardar la clave generada.
7. Configurar esa clave fuera de git.

En desarrollo:

```bash
dotnet user-secrets set "Email:SmtpHost" "smtp.gmail.com" --project src/RymReportes.Web
dotnet user-secrets set "Email:SmtpPort" "587" --project src/RymReportes.Web
dotnet user-secrets set "Email:UseStartTls" "true" --project src/RymReportes.Web
dotnet user-secrets set "Email:Username" "rym.application@gmail.com" --project src/RymReportes.Web
dotnet user-secrets set "Email:Password" "<app-password-de-gmail>" --project src/RymReportes.Web
dotnet user-secrets set "Email:From" "rym.application@gmail.com" --project src/RymReportes.Web
```

En Windows Server:

```powershell
[Environment]::SetEnvironmentVariable("Email__SmtpHost", "smtp.gmail.com", "Machine")
[Environment]::SetEnvironmentVariable("Email__SmtpPort", "587", "Machine")
[Environment]::SetEnvironmentVariable("Email__UseStartTls", "true", "Machine")
[Environment]::SetEnvironmentVariable("Email__Username", "rym.application@gmail.com", "Machine")
[Environment]::SetEnvironmentVariable("Email__Password", "<app-password-de-gmail>", "Machine")
[Environment]::SetEnvironmentVariable("Email__From", "rym.application@gmail.com", "Machine")
Restart-Service "RYM Reportes Natura"
```

## Ejecucion

```bash
dotnet run --project src/RymReportes.Web/RymReportes.Web.csproj --urls http://localhost:5085
```

Abre `http://localhost:5085`.

## Publicacion en Windows Server

Publica la aplicacion en Release para Windows x64. El paquete self-contained evita instalar el runtime de .NET en el servidor:

```powershell
dotnet publish .\src\RymReportes.Web\RymReportes.Web.csproj -c Release -r win-x64 --self-contained true -o C:\Services\RymReportes
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
