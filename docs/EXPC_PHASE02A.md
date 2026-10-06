# EXPC-Uninstaller — Phase 02A / .NET 10

Дата: 2026-10-06. Основа: `1b15b6daeb44435a16bd65e3c6436f3699b39818`, ветка `expc/phase-02a`.

## STATUS

Миграция, self-contained ZIP, тесты и запуск в чистой Windows Sandbox прошли. LIVE-проверены поиск, сортировка размеров в обе стороны и обновление. Проверка переключения Store и итоговый скриншот ожидают доступа к рабочему столу: Computer Use повторно вернул `GetCursorPos: access denied (0x80070005)`. Результаты Phase 02 не выдаются за повторную LIVE-проверку на .NET 10.

## Что изменено

- Оболочка и её тесты: `net10.0-windows10.0.18362.0`. Общие KlocTools и StoreAppHelper собираются для net8/net10; .NET 8 оставлен для старых проектов BCU. Выход net10-зависимостей отделён в `bin/EXPC-Dependencies`, старые выходы .NET 8 сохранены.
- `global.json`: SDK 10.0.401, `rollForward: disable`, `allowPrerelease: false`. Фактически выбран 10.0.401. SDK задаёт latest runtime 10.0.12 для self-contained публикации.
- Четыре runtime-свойства GlassButton получили `DesignerSerializationVisibility.Hidden`: обязательное исправление ошибки нового анализатора WinForms WFO1000. Логика кнопок и внешний вид не изменены.
- В существующие локальные замеры добавлены фактическая версия runtime и его каталог. Новых пользовательских функций нет. MainWindow, быстрый reader, иконки, палитра и UI-строки не переписаны.
- Версии пакетов не обновлялись: System.Management 8.0.0, Microsoft.NET.Test.Sdk 18.9.0, MSTest 4.4.0. Проверенные механизмы BCU совместимы со сборкой .NET 10; два net8-варианта зависимостей также успешно собраны.
- Старые формы KlocTools, не используемые оболочкой, при полной net10-сборке дают три предупреждения CS0672/WFDEV004 об OnClosing/OnClosed. Предупреждения не скрыты; общий рефакторинг BCU не выполнялся.

## Portable

ZIP: `C:\AI\EXPC-Uninstaller\artifacts\phase02a\portable\EXPC-Uninstaller-net10-win-x64.zip`.

Проверенный EXE после распаковки: `C:\AI\EXPC-Uninstaller\artifacts\phase02a\portable\unpacked\EXPC-Uninstaller.exe`.

ZIP: **58 790 209 байт**, SHA256 **`7F7F731056CD31B1223A39BD8F5D0AD61B40A58B1F17A62B0F2F0A6AE43E20E1`**. Распаковано 302 файла, 150 030 918 байт; каждый файл побайтно сверен с publish, расхождений 0. Архив создан до запусков: локальных логов, метрик, списка программ и кэша внутри нет.

Включены Microsoft.NETCore.App **10.0.12** и Microsoft.WindowsDesktop.App **10.0.12**. `coreclr.dll`, `hostfxr.dll`, `hostpolicy.dll`, WinForms и остальные runtime-файлы находятся рядом с EXE. Trimming и single-file выключены. Фактически загружено **.NET 10.0.12**, runtimeDirectory указывает на папку распаковки, а не системный .NET. Нельзя переносить один EXE отдельно от соседних файлов.

Вспомогательные EXE: StoreAppHelper имеет self-contained runtimeconfig net10 с обоими runtime 10.0.12; его managed entry point проверен командой `dotnet exec .../StoreAppHelper.dll /query`: exit 0, 126 записей. Его отдельный apphost требует администратора по исходному манифесту BCU, запуск apphost с UAC отдельно не проверялся. Оболочка использует `StoreAppHelper.AppManager.QueryApps()` внутри своего процесса; манифесты и защитные проверки BCU не ослаблены. `createdump.exe` — штатная часть runtime, отдельно не запускался. Старый GUI BCU и другие его helper EXE в ZIP не включены.

## Проверки и замеры

- .NET 10 build: PASS (нет ошибок; три описанных предупреждения общего KlocTools при полной компиляции).
- Тесты: **10/10**, target .NETCoreApp v10.0. Проверяют метаданные, точные дубли, поиск, сортировку, безопасные пути иконок. Это автоматические проверки логики, не замена LIVE-кликам.
- Форматирование изменённых C#, просмотр diff и поиск типовых секретов: PASS, совпадений secret-шаблонов нет. Исходные assets, Licence.txt, NOTICE и пользовательский desktop.ini сохранены без изменения содержимого. Локальные артефакты не коммитятся.
- Запуск распакованного EXE и чтение списка на рабочем столе: PASS, **384 записи**, ошибок 0, 96 DPI.
- LIVE на .NET 10: поиск `BCUninstaller` → 1 из 384; Escape → 384. Числовая сортировка размера по возрастанию и убыванию работает. F5 → 384 записи, ошибок 0, чтение и применение **21,67 мс**.

| Запуск / PID | Окно от старта процесса, мс | Заполненный список, мс | Чтение + применение, мс |
|---|---:|---:|---:|
| 1 / 47468 | 170,39 | 241,03 | 50,21 |
| 2 / 40500 | 170,03 | 236,48 | 46,37 |
| 3 / 63520 | 172,45 | 240,21 | 46,97 |

Медиана: **170,39 мс** до окна, **240,21 мс** до списка. Повторные запуски на текущем ПК без перезагрузки/очистки кэша; cold-start и слабый ПК не аттестованы. Точки измерения прежние: Shown/Update окна, затем Update таблицы до ленивых иконок. Отдельные предварительные процессы инструмента работали в изолированном контексте и видели 360 записей; они исключены из этих трёх сопоставимых замеров. Интерактивная копия запущена обычным пользовательским способом.

## Чистая среда без системного .NET

**PASS: Windows Sandbox**, отдельная Windows `10.0.26100.9550`, сеть отключена. Проверки `where dotnet`, реестра InstalledVersions, каталогов shared в Program Files и Program Files (x86) показали отсутствие системного .NET. Portable скопирован в `C:\Users\WDAGUtilityAccount\Desktop\EXPC` и запущен без установки зависимостей.

Само приложение зафиксировало `.NET 10.0.12` из этой папки, окно за **548,29 мс**, список из **3 записей** за **632,70 мс**, ошибок 0. Процесс PID 8744 подтверждён tasklist. Это инструментальное подтверждение запуска и списка; визуальный снимок Sandbox пока не получен. Первый сборщик результатов выдал ранний TIMEOUT из-за поведения timeout.exe без интерактивного stdin; штатный `wsb exec` дочитал уже созданные приложением метрики и подтвердил успешный результат. Исходный runtime рабочего компьютера не удалялся и не изменялся.

Локальные доказательства: `artifacts/phase02a/visible-startups.json`, `sandbox-results/system-runtime-check.txt`, `sandbox-results/startup-metrics.jsonl`, `sandbox-results/process.txt`, TRX в `artifacts/phase02a/tests`. Исходная API-граница Windows 10 18362 сохранена; это не аттестация всех версий Windows 10. Проверен win-x64, Windows 11 host/Sandbox; ARM64, x86, старые ПК и 150% DPI не проверялись заново.

## Команды сборки из корня репозитория

Нужен SDK **10.0.401** только на машине разработчика. Конечному пользователю нужен распакованный ZIP.

```console
dotnet --version
dotnet build source/ExpcUninstaller/ExpcUninstaller.csproj -c Release -p:Platform=x64 -p:GenerateSatelliteAssembliesForCore=true -p:RestoreSources=https://api.nuget.org/v3/index.json
dotnet test source/ExpcUninstallerTests/ExpcUninstallerTests.csproj -c Release -p:Platform=x64 -p:GenerateSatelliteAssembliesForCore=true -p:RestoreSources=https://api.nuget.org/v3/index.json
dotnet msbuild scripts/Build-ExpcPortable.proj -t:Package
dotnet msbuild scripts/Build-ExpcPortable.proj -t:Extract
```

Скрипт использует штатные publish/ZipDirectory/Unzip задачи SDK; PowerShell и отдельный архиватор не нужны. Существующий OutputRoot не перезаписывается: для повторной упаковки передать новый абсолютный путь через `-p:OutputRoot=C:/AI/EXPC-Uninstaller/artifacts/portable-next` в обе команды. Содержимое пакета воспроизводится закреплённым SDK; побайтовая идентичность ZIP между сборками не обещается (времена файлов/архива и SourceRevisionId).

Справочники: [SDK pinning](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json), [self-contained](https://learn.microsoft.com/en-us/dotnet/core/deploying/), [WinForms migration](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/migration/), [Windows Sandbox CLI](https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-cli).

## NEXT / STOP

Завершить повторную LIVE-проверку после доступа к рабочему столу. Удаление, зачистка, проверка версий, темы и языки остаются отключёнными. Никаких push/release и перехода к новой фазе.
