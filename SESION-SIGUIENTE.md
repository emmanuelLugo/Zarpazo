# Zarpazo: arranque de la siguiente sesión

Pega esto al empezar, o pide: «lee SESION-SIGUIENTE.md y ROADMAP.md y empieza».

## Dónde estamos
- Versión 0.10.0 publicada (commit `a02455d`). Rama `main`, árbol limpio al empezar el análisis.
- Hecho en la sesión anterior: análisis de funciones, pulido y propuestas. Resultado en `ROADMAP.md` (checklist en orden de recomendación). No se tocó código.
- `ROADMAP.md` y este archivo aún no están commiteados.

## Qué hacer
1. Leer `CLAUDE.md`, `PLAN.md` y `ROADMAP.md`.
2. Empezar por el punto 1 de la prioridad: **log de errores en archivo (rotativo) y botón «Copiar información para reportar» en Configuración > Acerca de**.
   - Hoy no hay manejo de `UnhandledException` ni log en disco.
   - Capturar `AppDomain.UnhandledException`, `Application.DispatcherUnhandledException` y `TaskScheduler.UnobservedTaskException`.
   - Carpeta del log junto a los demás datos de la app (ver `Settings/SettingsStore.cs`).
   - El reporte debe incluir versión, Windows, monitores y escalas, y las últimas líneas del log. Nada de datos personales ni rutas de capturas.
3. Al terminar: test en `tests/Zarpazo.Tests`, marcar `[x]` en `ROADMAP.md`, commit, y decirme cómo ejecutar y qué probar.
4. Seguir con el punto 2 (recorte posterior) solo cuando yo lo diga.

## Reglas (de CLAUDE.md)
- Bloques pequeños, un commit por bloque, indicar cómo ejecutar y qué probar.
- Confirmar conmigo antes de cualquier función marcada «Después» o «Avanzado» en PLAN.md.
- Código y nombres en inglés; textos de interfaz en español. Una clase por archivo.
- Compilar con el dotnet de 64 bits; si la app está abierta, se bloquea el exe (ver memoria `zarpazo-video-editor`).
- Los tests que dejan cambios pendientes cuelgan con un MessageBox.
