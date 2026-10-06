# EXPC-Uninstaller — Phase 01 baseline

Дата: 2026-10-06 (Europe/Moscow). STATUS: полная сборка solution и GUI smoke подтверждены; тестовый baseline имеет известное падение. Поведение исходников не изменялось.

## Исходная точка и сохранность

| Параметр | Факт |
| --- | --- |
| Рабочая папка | `C:\AI\EXPC-Uninstaller` |
| origin | `https://github.com/AlexHDman/EXPC-Uninstaller` |
| upstream | `https://github.com/BCUninstaller/Bulk-Crap-Uninstaller` |
| Исходная ветка | `origin/master` |
| Исходный SHA | `30da609384c98ba6e35c6530129541ea4ff3970a` |
| Рабочая ветка | `expc/phase-01` |
| Версия в исходниках | BCU 6.3 (`source/Directory.Build.props`) |

До получения исходников папка содержала только `assets/icon.ico` и `desktop.ini`, без `.git`. Поэтому вместо clone поверх непустой папки использованы `git init`, настройка remotes, `fetch origin master`, проверка дерева на совпадения путей, checkout `master` и новая рабочая ветка. Исходные файлы не перезаписаны. `upstream` настроен, но его HEAD не использовался как baseline и не сравнивался с форком.

Прочитаны инструкции пользователя, `README.md`, `CONTRIBUTING.md`, `NOTICE`, `Licence.txt`, solution, проектные и CI-файлы. `AGENTS.md` в репозитории и проверенных родительских папках не найден. Для Phase 01 действуют инструкции, переданные пользователем в чате.

Контрольные SHA256 локальных файлов до работы и после проверки совпадают:

| Файл | SHA256 |
| --- | --- |
| `assets/icon.ico` | `3FC0D66BEB7F1CDB05E4D825BBFD9938F79291AA142D86C83618317FB0D1B6A4` |
| `desktop.ini` | `0267F0A0B0444A5035DD5DC9D16D79AF7DC5BC7D20939361B4B2348B6E167341` |

Эти два файла остаются пользовательскими untracked-файлами и не входят в commit документации. `Licence.txt`, `NOTICE`, ресурсы BCU, заголовки авторства и исходники не изменялись.

## Фактический стек и проекты

Solution: `source/BulkCrapUninstaller.sln`. В нём 18 C# проектов, один C++ проект, один shared project и виртуальная папка Solution Items.

| Проекты | Назначение |
| --- | --- |
| `BulkCrapUninstaller` | WinForms GUI, выходной `BCUninstaller.exe`; главная форма, фильтры, настройки, подтверждения и оркестрация действий |
| `BCU-console` | CLI `list`, `export`, `uninstall`; JSON/XML вывод, общий движок |
| `UninstallTools` | Обнаружение, модель приложения, классификация деинсталляторов, запуск удаления, поиск остатков и автозапуск |
| `KlocTools` | Windows API, реестр, MSI, COM, общие инструменты и WinForms-диалоги |
| `ObjectListView`, `SimpleTreeMap` | Таблица и карта занимаемого места |
| `PortableSettingsProvider`, `NetSettingBinder` | XML-настройки рядом с приложением и привязка настроек к UI |
| `NBug` (`NBug_custom`) | Обработка ошибок |
| `StoreAppHelper`, `SteamHelper`, `OculusHelper`, `WinUpdateHelper`, `ScriptHelper` | Специализированные CLI helpers обнаружения и действий |
| `UniversalUninstaller`, `UninstallerAutomatizer` | Универсальное удаление и автоматизация внешних деинсталляторов |
| `BulkCrapUninstallerTests` | MSTest |
| `SimpleTreeMapTestApp` | Отдельное демонстрационное GUI-приложение, не unit-test suite |
| `HelperTools.shproj` | Общие исходники, включаемые через `.projitems`; отдельного EXE нет |
| `BCU-launcher.vcxproj` | Нативный C++ launcher; MSVC v143, static MFC, Win32/x64 конфигурации |

Общий target framework: `net8.0-windows10.0.18362.0`, `UseWindowsForms=true`, `Deterministic=true`, обычная сборка `SelfContained=false`. GUI и helpers используют общий admin-манифест `source/app.manifest`. Это не WPF, WinUI, Electron или Tauri. `UninstallTools` также связан с WinForms/ObjectListView: он не является независимой кроссплатформенной библиотекой.

Основные прямые NuGet-зависимости: Microsoft.VisualBasic 10.3.0, TaskScheduler 2.12.2, Newtonsoft.Json 13.0.4, FlaUI.Adapter.White 0.2.1, FlaUI.UIA3 5.0.0, System.Management, System.Drawing.Common, System.ServiceProcess.ServiceController. Часть System-пакетов задана диапазонами `[8.*,9)`.

При этой сборке разрешились System.Management 8.0.0, System.Drawing.Common 8.0.31, System.ServiceProcess.ServiceController 8.0.1; тесты используют Microsoft.NET.Test.Sdk 18.9.0 и MSTest 4.4.0. Есть COMReference `Scripting` в `UninstallTools` и Windows Update COM interop. Поэтому простого `dotnet build` недостаточно: требуется full-framework MSBuild с NETFX SDK tools.

## Сборка и окружение

Окружение проверки: x64, версия ОС, возвращённая .NET, `10.0.26300.0`. Установлены VS 2022 Build Tools / MSVC 14.44.35207 / Windows SDK 10.0.26100.0. Изначально доступный через `dotnet` SDK — 10.0.401; установленный WindowsDesktop runtime включает 8.0.31.

Первый запуск штатной сборки выявил ограничения машины:

1. VS MSBuild: `MSB4236`, не найден совместимый `Microsoft.NET.Sdk`; launcher: `MSB8041`, отсутствует MFC.
2. `dotnet build`: сначала `NU1100` из-за доступной конфигурации feeds, затем после явного NuGet.org — `MSB4803` (`ResolveComReference` не поддерживается .NET Core MSBuild).
3. Локальный SDK8: resolver необязательных workloads не разрешался; выключен только в окружении процесса сборки.
4. Не хватало `al.exe` и `AxImp.exe` (`MSB3086`, `MSB3091`). Подготовлены официальные NETFX SDK tools локально.

Системные SDK и Visual Studio не переустанавливались. В игнорируемой `artifacts/phase01` размещены:

- .NET SDK **8.0.425**, официальный win-x64 ZIP; SHA512 сверён с release metadata Microsoft.
- NETFX SDK **4.8.1**, пакет VS `Microsoft.Net.4.8.1.SDK` версии **4.8.9037.0**. MSI прочитан как таблица имён, CAB извлечён; установка MSI не выполнялась. Выделены x64 `al.exe`, `al.exe.config`, `AxImp.exe`, `TlbImp.exe`.
- MFC **14.44.35208**, официальный пакет `Microsoft.VC.14.44.17.14.MFC.X86.base`. VSIX извлечён как ZIP; SHA256 сверён с каталогом VS. Для Unicode launcher указан фактический локальный `mfcs140u.lib` через `MFC_KeyFile`; защита проверки наличия библиотеки не отключалась.

Управляемая сборка: **18/18 проектов, exit 0**. Launcher: **exit 0**, `bin/launcher/BCU-launcher.exe`. Не применялись изменения C#/C++ исходников, проектов, манифеста, уровня защиты или UI. Временная попытка использовать C# compiler для satellite DLL не была окончательным способом: финальная конфигурация использует штатный `al.exe` из локального SDK.

Итог общей сборки solution `Release|x64`: **PASS, exit 0**, включая Win32 launcher. Лог: `artifacts/phase01/build-solution-complete-x64.log`. Тестовый проект дополнительно собран отдельным вызовом среди 18 C# проектов. Промежуточный запуск при открытом тестовом GUI остановился на `MSB3027/MSB3021`: процесс держал `Interop.Scripting.dll`. После подтверждения завершения процесса повтор завершился успешно.

### Повтор сборки с подготовленными локальными зависимостями

PowerShell, cwd `C:\AI\EXPC-Uninstaller`. Перед сборкой закрыть BCU из этой папки.

```powershell
$env:DOTNET_ROOT = 'C:\AI\EXPC-Uninstaller\artifacts\phase01\dotnet8'
$env:DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR = $env:DOTNET_ROOT
$env:MSBuildSDKsPath = Join-Path $env:DOTNET_ROOT 'sdk\8.0.425\Sdks'
$env:MSBuildEnableWorkloadResolver = 'false'
$env:PATH = $env:DOTNET_ROOT + ';' + $env:PATH
$msbuild = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\amd64\MSBuild.exe'
$netfx = 'C:\AI\EXPC-Uninstaller\artifacts\phase01\netfxsdk\tools'
$mfc = 'C:\AI\EXPC-Uninstaller\artifacts\phase01\mfc-x86\Contents\VC\Tools\MSVC\14.44.35207\atlmfc\lib\x86\mfcs140u.lib'
& $msbuild source\BulkCrapUninstaller.sln /restore /m `
  /p:Configuration=Release /p:Platform=x64 `
  /p:TargetFrameworkSDKToolsDirectory=$netfx /p:MFC_KeyFile=$mfc `
  /p:RestoreSources=https://api.nuget.org/v3/index.json /verbosity:minimal
```

Для подготовки чистой машины нужны эквивалентные .NET 8 SDK, .NET Framework SDK tools, C++ v143/MFC и Windows SDK. Локальные архивы не являются частью Git-репозитория. Официальные источники: [.NET 8 release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/8.0/releases.json), каталог установленного VS; документация [ResolveComReference](https://learn.microsoft.com/en-us/visualstudio/msbuild/resolvecomreference-task) и [параметры .NET SDK](https://learn.microsoft.com/dotnet/core/tools/csproj).

Логи сборок: `artifacts/phase01/build-*.log`. Сборка с `Deterministic=true` ещё не доказывает воспроизводимость: нет закреплённого SDK в `global.json`, часть NuGet-версий плавающая, lock-файлы не закреплены; побитовое сравнение двух чистых сборок не выполнялось.

## Запуск, чтение списка и тесты

| Проверка | Результат |
| --- | --- |
| EXE GUI | `C:\AI\EXPC-Uninstaller\bin\Release\x64\BCUninstaller.exe` |
| Запуск именно локальной сборки | **PASS**: Computer Use вернул окно с полным путём EXPC-Uninstaller, заголовок `Bulk Crap Uninstaller v6.3 Портативная x64` |
| Отображение списка | **PASS**: после активации видны заполненная таблица, статус `Готов`, счётчик **366 деинсталляторов** |
| Полнота списка / Store / права | **PARTIAL**: число записей не доказывает полноту; были отказы доступа к HKLM при обработке автозапуска и ошибка определения Steam |
| CLI JSON smoke | **BLOCKED**: прямой запуск EXE с перенаправлением требует elevation; запуск DLL через dotnet дошёл до сканирования, но helper потребовал elevation (740). Скан остановлен по проверенному PID/командной строке; валидный JSON не получен |
| Удаление программ, остатков, записей | **NOT TESTED**, команды удаления не вызывались |
| Portable без установленного runtime | **NOT TESTED**: текущий build framework-dependent; portable ZIP в этой фазе не выпускался |
| Скорость старта, старые ПК, другие ОС/архитектуры | **NOT TESTED**, достоверного временного benchmark нет |

На машине уже был открыт установленный BCU. Пользователь закрыл его, поскольку `EntryPoint.cs` использует глобальный mutex `Global\BCU-singleinstance`. Первый доступ Computer Use завершился тайм-аутом app approval; последующий доступ и активация подтвердили реальную таблицу. Позднее автоматизация активации окна стала недоступна, поэтому для освобождения DLL запрошено обычное закрытие пользователем. Перед финальной сборкой отсутствие окна и завершение записанного PID проверены; тестовые GUI/CLI не оставлены работающими.

Безопасность smoke: перед запуском только build-копия `CleanLogs.bat` временно перенесена в игнорируемую artifacts. В исходном BCU `EntryPoint.ProcessShutdown` запускает этот скрипт при выходе portable-копии; он удаляет BCU-логи и .NET-папки вне каталога EXE. Этот побочный эффект исключён из проверки. Исходный скрипт не редактировался. После завершения GUI копия возвращена, хэш сверён с исходником, скрипт не запускался. Сборка повторно копирует скрипт в output, поэтому перед каждым smoke это нужно учитывать.

Обнаружение BCU не является гарантированно read-only API: `NewStartupDisable.GetDisabled`, `BrowserEntryFactory` могут открывать/создавать registry keys; в первом логе зафиксирован отказ доступа. Не утверждается, что BCU вообще не пишет на систему: он также сохраняет собственные настройки и кэш. В Phase 01 не вызывались действия удаления или очистки пользовательских данных.

Существующие локальные тесты:

```powershell
dotnet test source\BulkCrapUninstallerTests\BulkCrapUninstallerTests.csproj `
  -c Release -p:Platform=x64 --no-build --no-restore `
  --filter 'FullyQualifiedName!~UninstallerRatingManagerTests' `
  --logger 'trx;LogFileName=baseline-offline.trx' `
  --results-directory artifacts\phase01\TestResults
```

Результат: **52 запущено, 51 PASS, 1 FAIL, 0 skipped внутри выбранной выборки**. Четыре метода `UninstallerRatingManagerTests` исключены фильтром как зависящие от внешнего сервиса рейтингов; это не PASS и не четыре runner-skipped теста.

Падение: `ApplicationListConstantsTests.GetApplicationIntegrityText_ReturnsGoodWhenEntryIsRegisteredAndValid`. Ожидалось `Хорошо` из `UninstallTools.Properties.Localisation.Confidence_Good`, фактически `Good` из `BulkCrapUninstaller.Properties.Localisable.IntegrityColumn_Good`. У нового GUI-ресурса нет соответствующей русской записи. Это baseline-дефект/расхождение тестового контракта; в Phase 01 не исправлялся. TRX и лог: `artifacts/phase01/TestResults/baseline-offline.trx`, `artifacts/phase01/tests-offline.log`.

Есть существующие compiler warnings CS0436 о дублировании shared HelperTools types в тестовом проекте. Отдельной настроенной lint-команды/`.editorconfig` не найдено; проверка документации — Markdown-структура и `git diff --check`, без форматирования старых исходников.

Контрольные SHA256 проверенных бинарников:

| Файл | SHA256 |
| --- | --- |
| `bin/Release/x64/BCUninstaller.exe` | `84E30B8C75C2CE54FE6666B6D65CDE6B62B2D890A866DA6DF9CFBA17F5366EFC` |
| `bin/Release/x64/BCUninstaller.dll` | `64892D3E7EECF07CC9455C58B028D5CBBD3CEA2D6391B3080283068DF7E64A13` |
| `bin/launcher/BCU-launcher.exe` | `C00F2AA5BB503CBF3457EC39F4383E118F0B9BC4CE7A304146E2C54806419415` |

Полные списки программ, runtime settings, кэш, screenshots с названиями ПО и логи не включаются в commit. В документации остаются только агрегаты и технические ограничения.

## Повторное использование механизмов BCU

| Область | Реальная точка входа / ограничение |
| --- | --- |
| Обнаружение | `UninstallTools/Factory/ApplicationUninstallerFactory.GetUninstallerEntries(callback)`: MSI, реестр, диски, прочие factories, объединение, InfoAdders, кэш и автозапуск |
| Источники | Registry, Directory, Store, Steam, Oculus, Chocolatey, Scoop, Windows Features/Updates, Script/Predefined factories; доступность зависит от конфигурации и helpers |
| Модель | `ApplicationUninstallerEntry`: DisplayName, EstimatedSize, InstallDate, DisplayVersion, AboutUrl, UninstallerKind, IsProtected, IsValid, RegistryPath и др. Значения размера/даты могут быть отсутствующими или оценочными |
| UI обновление | `Functions/ApplicationList/UninstallerListViewUpdater.cs`: фоновой поток, progress, отмена, AllUninstallers/FilteredUninstallers |
| Удаление | `Functions/AppUninstaller.cs` → проверки, wizard, restore point, `UninstallManager.CreateBulkUninstallTask` / `RunUninstaller`; helpers и автоматизация внешних деинсталляторов |
| Остатки | `JunkManager.FindJunk(targets, allUninstallers, callback)` → scanners, confidence, исключение известных системных папок/самого BCU; `JunkRemoveWindow` → выбор, предупреждения и backup; `IJunkResult.Delete` — отдельное опасное действие |
| Записи реестра | Отдельные UI-ветки MainWindow, проверки защищённости; не подменять обычное удаление стиранием записи |
| Локализация | `.resx` и satellite DLL; `CultureConfigurator`, `Settings.Language`; ручной выбор уже сохраняется. Исходник использует CurrentCulture, что не всегда равно языку Windows UI |
| Portable | `Program.PrepareSettings` → `BCUninstaller.settings` рядом с EXE/launcher; `PortableSettingsProvider`; `InfoCache.xml` рядом с core assembly. `AppLocation` имеет отдельную проверку имени `win-x`, требующую внимания для ARM64 |
| Актуальность | `UpdateGrabber` проверяет релиз самого BCU. Готового общего сервиса актуальности установленных программ WinGet/сайты/GitHub не найдено |

Минимальный путь: новая главная форма EXPC в существующем WinForms GUI-проекте, общий `UninstallTools` и существующие helpers. На первом POC оболочки подключать только список; точку входа переключать после согласования макета. Не выделять сразу новый процессный протокол и не переносить движок на другой стек. CLI JSON пригоден как диагностический путь, но текущие elevation/helper-зависимости не делают его более простым вариантом полноценной оболочки.

Прямой вызов низкоуровневого `RunUninstaller` недостаточен для сохранения всех защит. Проверки защищённых записей, работающих процессов, restore point, уверенности в остатках и резервного копирования распределены между библиотекой, `AppUninstaller`, `MainWindow`, `BeginUninstallTaskWizard` и `JunkRemoveWindow`. Упрощение UI должно сохранить эти проверки.

## Архитектуры, упаковка и лицензии

`Directory.Build.props`: AnyCPU, x64, x86, ARM64; RID win-x64/win-x86/win-arm64. `publish.bat` выпускает self-contained x64 и ARM64, а также framework-dependent AnyCPU; ветка x86 закомментирована. В solution x64 и ARM64 используют Win32 launcher. Реально в Phase 01 собраны/запущены x64 managed GUI и собран Win32 launcher; x86/ARM64 managed и запуск launcher **NOT TESTED**.

Target framework указывает Windows 10 API 18362; это ещё не проверенная матрица минимальных ОС. Требование Windows 10/11 на старых ПК остаётся продуктовым, а не подтверждённым для всех сборок.

Существующий CI в `.github/workflows/ci.yaml` собирает Any CPU на windows-2022, публикует framework-dependent artifact и запускает тесты. Это пока не EXPC portable release pipeline. Workflow публикации WinGet содержит исходный identifier `Klocman.BulkCrapUninstaller`: до любого будущего релиза его нужно отдельно адаптировать к EXPC. Никакие workflow или релизы в этой фазе не запускались.

Корневой `Licence.txt` — Apache 2.0, `NOTICE` сохраняет авторство Marcin Szeniak и ссылки BCU. В `ObjectListView.cs` есть уведомление GPL v3-or-later; `PortableSettingsProvider.cs` указывает CPOL 1.02; NBug содержит MIT LICENSE. Эти исходные уведомления сохранены. Это перечень обнаруженных уведомлений, не заключение о совместимости лицензий; перед распространением проверить состав дистрибутива и комплект third-party notices.

## NEXT — один практический этап

**Phase 02: визуальный макет одного главного окна на синтетическом списке**, в первую очередь Glass Teal, с утверждёнными колонками, меню, кнопками, поиском, вкладками и прямыми списками языка/темы. Проверить читаемость и компактность на обычном экране/масштабировании, показать пользователю и получить принятие макета. Не подключать удаление, не менять ядро и не начинать реализацию оболочки до согласования. Затем STOP.

Phase 01 заканчивается локальным commit только двух документов. Без push, tag, release и публикации.
