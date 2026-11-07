# Cómo colaborar

Gracias por querer ayudar. Esta guía explica cómo se organiza el proyecto y cómo proponer cambios.

## Antes de empezar

- Para un fallo, abre un [issue](https://github.com/emmanuelLugo/Zarpazo/issues/new?template=bug_report.yml) con los pasos para reproducirlo.
- Para una función nueva, abre primero un [issue de mejora](https://github.com/emmanuelLugo/Zarpazo/issues/new?template=feature_request.yml). El plan está en [PLAN.md](PLAN.md): lo marcado *Después* o *Avanzado* se acuerda antes de escribir código.
- Los cambios pequeños (un error, un texto) pueden ir directo a un pull request.

## Preparar el entorno

Necesitas Windows 10 u 11 y el [SDK de .NET 8](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
git clone https://github.com/emmanuelLugo/Zarpazo.git
cd Zarpazo
dotnet run --project Zarpazo
dotnet test
```

Las pruebas de grabación usan el escritorio real y tardan cerca de un minuto. Para el instalador hace falta además [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`).

## Estructura

| Carpeta | Contenido |
| --- | --- |
| `Zarpazo/Capture` | Captura de pantalla y detección de ventanas |
| `Zarpazo/Overlay` | Ventana de selección sobre el escritorio virtual |
| `Zarpazo/Editor` | Anotaciones vectoriales y barra de herramientas |
| `Zarpazo/Output` | Copiar, guardar, guardado automático y Embellecer |
| `Zarpazo/Recording` | Grabación de GIF y MP4, audio y recorte |
| `Zarpazo/History`, `Pin`, `Ocr`, `Settings`, `Shell` | Historial, fijar, OCR, configuración, atajo global y bandeja |
| `tests/Zarpazo.Tests` | Pruebas xUnit |
| `tools/` | Scripts de publicación e instalador |

## Convenciones

- **Código y nombres en inglés; textos de la interfaz en español.**
- Una clase por archivo. La lógica de captura, el overlay y el editor viven en carpetas distintas.
- Las anotaciones son objetos vectoriales, no píxeles pintados, para poder deshacer, rehacer y mover.
- El overlay se muestra solo cuando la captura ya está lista (evita el parpadeo) y cubre el escritorio virtual completo, con coordenadas negativas y distinto escalado por monitor.
- Las pruebas que dependen del tiempo (grabación, codificación) van en la colección xUnit `Recording`, que se ejecuta en serie.
- Cada cambio de comportamiento lleva su prueba cuando es razonable.

## Ramas y commits

- `main` siempre compila y pasa las pruebas. Trabaja en una rama (`feature/lo-que-sea`, `fix/lo-que-sea`) y abre un pull request hacia `main`.
- Un commit por bloque de trabajo, con un mensaje en inglés que describa el resultado en una frase: `Trim a recorded video: choose the start and end in a small player`.
- Los pull requests pequeños y enfocados se revisan más rápido.

## Publicar una versión (mantenedor)

1. Sube `<Version>` en `Zarpazo/Zarpazo.csproj` y mueve las notas de `[Sin publicar]` a una sección nueva en `CHANGELOG.md`.
2. Haz commit en `main` y crea la etiqueta: `git tag v0.2.0 && git push origin v0.2.0`.
3. El workflow `Release` compila, genera el instalador y los ejecutables, y crea la release en GitHub con las notas del changelog. Falla si la etiqueta no coincide con la versión del proyecto.

El `AppId` de `installer/Zarpazo.iss` no debe cambiar nunca: es como Windows sabe que una versión nueva reemplaza a la anterior.
