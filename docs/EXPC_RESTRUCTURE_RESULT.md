# EXPCdelete — структура, очистка и compact portable, 2026-10-26

STATUS: CLI BUILD/TEST/PACKAGE VERIFIED. LIVE GUI/DPI/запуск приложения NOT TESTED.
Финальное состояние: **активное пользовательское тестирование и сбор багов; функциональная разработка на паузе**.

Корень C:\AI\EXPC-Uninstaller; ветка expc/updates-vault; HEAD 71d8390e09305e8148dd16bbf8bdf238923638c3. Работа не закоммичена. Producer — текущее дерево с переносами, не чистый HEAD; snapshots producer-inputs.json находятся в каталоге каждого успешного build-id. Запрошены GPT-6 Astra / High; точный runtime model/effort через доступные средства не подтверждён, переключение не заявляется. Агент Codex, локальный Windows terminal, HEADLESS/BACKGROUND.

## Выдача

- `C:\AI\EXPC-Uninstaller\dist\EXPCdelete.exe`
- Размер: **9,344,512 байт**; SHA256 **50e2bef78b24047a0e988fd72c804be1c9e698c8c960795355efe1d5d187d61d**.
- Compact, не self-contained: приложение полностью внутри, системный Microsoft.WindowsDesktop.App 10 x64 от 10.0.12 остаётся согласованной зависимостью. Никаких сетевых подгрузок частей приложения не добавлено.
- В dist ровно один EXE. Манифест/inspection/87 тестов: `artifacts/verification/20261026-194524/`. Контроль предыдущего кандидата: `artifacts/archive/control/71d8390e/EXPC-Uninstaller-Compact.exe`, SHA256 `6cc787631eaa0088ca30d06ea88a428dff9d48efb1c08a235abf47b5add24617`.

Штатная повторная сборка:

```powershell
pwsh -NoProfile -File C:\AI\EXPC-Uninstaller\scripts\Build-EXPCdelete.ps1
```

C#/C++ → чистый проверяемый build/staging → unit/native-policy/static-payload gates → атомарная замена dist. Прежний отличающийся dist архивируется с SHA256. Неизвестный/изменённый файл staging вызывает отказ без удаления; отрицательный тест подтвердил сохранность dist, неизвестного файла и staging manifest. Есть блокировка параллельной сборки и таймауты внешних команд. SDK 10.0.401 закреплён; версии runtime/library не обновлялись.

## Итоговая структура

```text
src/                         C#, C++ launcher, проекты и upstream solution
assets/                      оригиналы icon.ico и UI_Master_Visual.png
tests/                      тестовые проекты, fixture, console extraction harness
scripts/                     единая сборка, упаковка и проверки
docs/                        требования, отчёты, checkpoint/backlog, upstream-материалы
build/                       staging, bin/obj и необходимые toolchain-файлы
artifacts/verification/      доказательства по идентификатору сборки
artifacts/archive/           контрольный portable, rollback и уникальные материалы
dist/EXPCdelete.exe           единственный актуальный распространяемый EXE
```

Карта: docs/EXPC_RESTRUCTURE_MAP.md. source → src; тестовые проекты → tests; tools launcher/probe → src, fixture → tests. doc → docs/upstream/manual; старые installer-исходники → docs/upstream/installer, новый installer не создавался. Старые publish/verify/workflows сохранены как исторические материалы docs/upstream; активные entry points используют новый путь. Действующий CI адаптирован под portable; BCU WinGet release workflow отключён, ни CI, ни публикация не запускались.

Исключения обоснованы: src/BulkCrapUninstaller.sln остаётся рядом с upstream проектами для SolutionDir; стандартные obj и standalone bin допустимы. **build/toolchain защищён**: dotnet8, MFC и NETFX для воспроизведения BCU; нельзя чистить build целиком. Неизвестный `%ProgramData%` и пользовательский desktop.ini остаются в корне. Это соглашение данного проекта, не общий уже внедрённый стандарт EXPC.

## Измеримый состав и оптимизация

| Метрика | До | После |
|---|---:|---:|
| EXE, байт | 11,695,616 | 9,344,512 |
| Сжатый payload, байт | 11,180,056 | 8,828,310 |
| Сумма распакованных файлов, байт | 55,802,527 | 55,381,152 |
| EXE вне payload, байт (launcher + PE/icon/manifest/alignment) | 515,560 | 516,202 |
| Число файлов payload | 51 | 30 |

Уменьшение EXE: **20.10%**. Основной выигрыш — LZMS block 64 MiB вместо default; формат, Windows cabinet decompressor, SHA256, NoLinks/RelativeName, CREATE_NEW и mutex не изменены. Дополнительного распаковщика или лицензируемой библиотеки в EXE нет.

Проверена полноценная Release-компиляция без PDB и embedded debug перед publish. Выявлена и устранена именно проблема сборки: промежуточные DLL тестов с embedded debug иногда попадали в incremental publish. Теперь build --no-incremental с теми же Release-параметрами предшествует publish --no-build. /Brepro для C++ убирает различия linker timestamp. Два финальных чистых staging-выпуска 20261026-194408 и 20261026-194524 дали одинаковый SHA256 выше.

Удалены из поставки только 21 неиспользуемый языковой satellite KlocTools. Обоснование: Localization.Select устанавливает только ru/en; английские neutral resources и русский satellite сохранены. Исходные переводы vendor не удалялись; системные .NET resources не трогались. Все 7 сторонних DLL, включая обе разные Microsoft.Windows.SDK.NET.dll, побайтно совпали с baseline. Никакого blind DLL removal, trimming, NativeAOT, обфускации или внешнего EXE-пакера.

Крупнейшие элементы: Microsoft.Windows.SDK.NET.dll 24,421,432 байт и package-probe/Microsoft.Windows.SDK.NET.dll 26,341,408 байт. Это разные версии для main и WinGet COM helper, не взаимозаменяемые дубликаты. Сохранены helper EXE, WinRT и COM projection, System.Management, native DLL, licenses/NOTICE. Динамический запуск helper и WinRT/COM требуют этих компонентов; отсутствие прямого C# вызова не использовалось для удаления.

Исходная графика сохранена по SHA256. ICO содержит 9 размеров 16–256. Lossless PNG-перепаковка дала 45,952 вместо 180,843 байт вне контейнера, с идентичными RGBA-пикселями, но LZMS сжимает исходник до 45,504 байт против 45,952 у PNG-варианта: **прототип отклонён**, визуальные ресурсы поставки не изменены. Объём managed resources EXPC baseline 229,696 байт; он не является главным источником размера. Resource/content dedup уже присутствует в packer и сохранён.

LZMA2 preset6 сравнен только прототипом: 8,179,780 байт на прежнем raw payload, декодирование около 442 мс против 221 мс для LZMS64 в том же эксперименте. Он не внедрён: требует дополнительного декодера/лицензионной проверки и проигрывает по времени. Ориентир 40% не достигнут; ради него не менялись SDK/архитектура, зависимости, функции или графика.

## Скорость — отдельный console harness, не запуск приложения

Нативный harness включает тот же неизменённый Extract из launcher, читает выбранный EXE через LoadLibraryEx DATAFILE/IMAGE_RESOURCE и не вызывает его entry point, UI или runtime installer. Кэши теста внутри build, пользовательский PortableCache не затронут. Включены SHA payload/каждого файла, запись/FlushFileBuffers для холодного кэша и проверка файлов для тёплого.

| Проверка, медиана 3 запусков | До, мс | После, мс |
|---|---:|---:|
| Пустой кэш: распаковка + целостность + запись | 423.008 | 343.555 |
| Повторный кэш: распаковка + проверка существующих файлов | 502.099 | 439.211 |

Это сопоставимые локальные фоновые замеры с файловым кэшем ОС, не обещание времени на другом ПК. Время появления окна/списка и полный старт/повторный старт приложения **NOT MEASURED**. Raw измерения: artifacts/verification/restructure-20261026/extraction-benchmark.json. Повторная проверка всё ещё декодирует payload — архитектура не менялась.

## Очистка и сохранённое

- Удалено **7,736 файлов / 5,938,934,440 байт**: все сначала перечислены с путём/размером/SHA256, затем перепроверены перед пофайловым удалением.
- Из них существовавшие до задачи материалы: **4,064,279,008 байт**. Созданные и затем удалённые временные эксперименты/кэши: **1,874,655,432 байт**. Последнее не прибавляется к экономии от исходного дерева.
- Удалены старые bin/obj, publish/unpacked DLL, payload/object и 18 устаревших ZIP после проверки состава и CRC. Нет слепого удаления родительских каталогов, git clean/reset, reparse переходов или остановки чужих процессов.
- Архив оставлен объёмом **918,002,691 байт**: точная таблица archive-sizes.csv. Причины: контроль 71d8390e; исторические A/B EXE; Phase02A и Phase03 verified ZIP для отката; офлайн SDK/MFC; manager/MSIX test inputs; уникальные логи, screenshots, manifests, конфиги/хранилища и доказательства. Сомнительное содержимое сохранено, а не признано мусором по возрасту.
- Полные удалённые перечни: cleanup-approved.csv, cleanup-second-approved.csv, obsolete-zip-candidates.csv, cleanup-final-approved.csv и соответствующие *-deleted.jsonl в artifacts/verification/restructure-20261026.
- Неизвестные/сохранённые: preserved-uncertain.json, kept-archives.json. Оставшиеся воспроизводимые dev/staging-файлы: remaining-reproducible-candidates.csv — **318 файлов / 230,792,497 байт**; они оставлены для продолжения разработки и диагностики. Новых удалений по этому списку не выполняется.
- Не изменены исходные assets, пользовательские данные, ключи/хранилища, лицензии, общие C:\AI инструкции и внешние проекты/диски. Два существующих процесса EXPC с origin вне проекта не останавливались.

## Проверки и ограничения

PASS: C# + native C++ build, 87/87 выбранных существующих/двух новых rename cases, native policy assertions, сборка трёх вспомогательных проектов, 51 путь проектов/solution, Python/PowerShell syntax, git diff --check. Функциональные .cs/.cpp/.h/.resx побайтно сохранены по карте переносов; изменения ограничены проектами/путями/упаковкой и отдельными тестами упаковки.

PASS: статическое чтение final embedded payload, всех SHA256, обязательных зависимостей и лицензий; 30 записей без test/development/private-data markers; отсутствие PDB и private-key markers. Это проверка состава, не математическое доказательство отсутствия любых возможных секретов. В пакет не копируются user configs или локальные хранилища.

PASS: tampered cache rejected; parallel extraction 2/2; тест разрешения portable data root для EXPCdelete.exe (включая пробелы). Launcher получает origin через GetModuleFileNameW и передаёт EXPC_PORTABLE_ORIGIN; внутренние имена оставлены прежними. Нет переноса runtime-данных в LocalAppData: он остаётся backlog. При ручном запуске данные, как раньше, создаются рядом с внешним EXE.

NOT TESTED: реальный старт переименованного EXE/GUI/DPI/UAC/runtime-install/реальные удаления/обновления; полный GUI-зависимый набор; CI на GitHub; полная donor BCU solution с MFC. Применимые управляемые EXPC и C++ сборки проверены локально; unit/harness PASS не заменяет ручную приёмку.

Никакого commit/push/merge/tag/release. Следующее действие — ручная проверка пользователем dist/EXPCdelete.exe, затем сбор наблюдений в backlog; функциональная разработка на паузе.

## Контроль размера дерева

Начальная инвентаризация без .git: 5,933,845,741 байт. Финальный снимок без .git: 2,192,219,262 байт; .git отдельно 18,822,139 байт. Полный размер проекта на момент снимка: 2,211,041,401 байт (2.059 GiB). Чистое уменьшение сопоставимого дерева без .git: 3,741,626,479 байт. Последующие текстовые отчёты добавляют небольшой объём. Размеры логические, не точный прирост свободных NTFS-кластеров. Reparse points: 0.
