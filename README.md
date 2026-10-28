# EXPC Uninstaller / EXPCdelete

Portable-оболочка на механизмах BCU. Текущий этап: активное пользовательское тестирование и сбор багов; функциональная разработка на паузе. Реорганизация 2026-10-26 не меняет функции и темы.

## Получить текущий кандидат

`dist/EXPCdelete.exe` — единственный актуальный распространяемый файл. Это компактный portable: всё приложение внутри, кроме Microsoft .NET Desktop Runtime 10 x64 (минимум 10.0.12, стабильные последующие патчи 10.0 допустимы). Нативный launcher сохраняет существующую проверку runtime. Установщик EXPC отложен.

```powershell
pwsh -NoProfile -File C:\AI\EXPC-Uninstaller\scripts\Build-EXPCdelete.ps1
```

Требуются Windows x64, SDK из global.json (10.0.401), Python 3.12+, PowerShell 7, MSVC Build Tools с Windows SDK и доступный NuGet restore. Команда собирает C#/C++, выполняет безопасные тесты, проверяет embedded payload как данные и только затем атомарно заменяет dist. Предыдущий отличающийся EXE сохраняется с SHA256 в artifacts/archive/control. Приложение автоматически не запускается.

Staging находится в build/staging. Повторный запуск удаляет только записанные и не изменённые файлы собственного staging; постороннее содержимое блокирует сборку. Защита от параллельных сборок — build/staging.lock. После аварии проверять процесс и журнал, не удалять lock вслепую. Манифест, хеши входов, команды и тесты — artifacts/verification/<build-id>. dist не содержит служебных файлов.

## Структура

- src — C#, C++ launcher, необходимые проекты и upstream solution. Внутренняя структура BCU сохранена.
- assets — исходная пользовательская графика; не перезаписывать.
- tests — тестовые проекты, изолированные fixtures и консольный тест распаковщика.
- scripts — сборка, упаковка, проверки. Sandbox-скрипты запускать только в отдельно разрешённой изоляции.
- docs — требования, checkpoint/backlog, отчёты; upstream/manual и upstream/installer — сохранённые материалы BCU, не новый installer.
- build — промежуточные результаты, staging и тестовые кэши. **build/toolchain — защищённые офлайн SDK/MFC/NETFX, не удалять при очистке build.**
- artifacts/verification — доказательства по build-id; artifacts/archive — контрольные кандидаты и классифицированная история.
- dist/EXPCdelete.exe — стабильная точка выдачи.

Стандартные obj остаются рядом с проектами; отдельные самостоятельные проекты сохраняют SDK bin по умолчанию. Общая upstream solution остаётся в src для совместимости SolutionDir. Неизвестные корневые %ProgramData% и пользовательский desktop.ini сохранены до отдельного решения. Это структура данного проекта, не объявление общего стандарта EXPC.

## Пользовательские данные и ограничения

Переименован только внешний распространяемый EXE. Внутренние имена сборок сохранены. EXPC_PORTABLE_ORIGIN по-прежнему определяет каталог portable; данные остаются рядом с внешним EXE. Миграция в LocalAppData отложена. При переносе пользовательской копии сохраняйте её local-data и резервные копии; сборщик их не переносит и не перезаписывает. После ручного запуска рядом с EXE могут появиться runtime-данные — правило «только EXE в dist» относится к выдаваемому пакету, не меняет действующее поведение приложения.

Никаких автоматических запусков GUI и установки/удаления рабочих программ. Публикация 6.3.1 отдельно разрешена пользователем. GUI/DPI/реальный старт нового имени: NOT TESTED. Перед работой читать C:\AI\AGENTS.md и C:\AI\EXPC_DEVELOPMENT_PROTOCOL.md, затем локальные AGENTS.md/CLAUDE.md.

Карта переносов: [docs/EXPC_RESTRUCTURE_MAP.md](docs/EXPC_RESTRUCTURE_MAP.md). Текущее состояние: [checkpoint](docs/EXPC_CHECKPOINT.md). Отложенные задачи: [backlog](docs/EXPC_BACKLOG.md). Итог и ограничения: [отчёт](docs/EXPC_RESTRUCTURE_RESULT.md).

Авторство BCU, Licence.txt и NOTICE сохранены. Исходный README BCU: docs/upstream/README-BCU.md. Исторические отчёты сохраняют старые пути; используйте карту переносов, а не переписывайте историю.

## Self-update / самообновление (6.3.1)

Справка → **Обновить программу** проверяет стабильные выпуски собственного [GitHub-репозитория](https://github.com/AlexHDman/EXPC-Uninstaller/releases/latest) только по команде пользователя. Старые EXE без этой функции замените вручную один раз; закройте старые экземпляры. Удаление/очистка/установка блокируют самообновление. Крестик и Alt+F4 теперь закрывают приложение; повторный запуск новой версии активирует существующий экземпляр в этой пользовательской сессии.

Help → **Update application** checks stable EXPC releases only on request. Older binaries require one manual replacement. Close existing old instances first. Build and fixture evidence do not replace real GUI acceptance. See [6.3.1 report](docs/EXPC_RELEASE_6.3.1.md).

Release metadata is generated beside verification evidence, not beside dist/EXPCdelete.exe: EXPCdelete.update.json and SHA256SUMS.txt. EXPC.Version.props is the version source. No independent EXPC installer is built.

## Локальный кандидат 6.3.2: кэш и диагностика

Распаковка и staging самообновления используют `%LOCALAPPDATA%\EXPC\Uninstaller\Cache`. Метрики по умолчанию выключены; для явной диагностики перед ручным запуском установите переменную процесса `EXPC_DIAGNOSTICS=1`. Файл будет в `%LOCALAPPDATA%\EXPC\Uninstaller\Logs\startup-metrics.jsonl`, без fallback рядом с EXE. Постоянные local-data/vault и обязательный журнал операций этим изменением не переносятся. Старые файлы автоматически не удаляются. См. [отчёт](docs/EXPC_CACHE_DIAGNOSTICS_6.3.2.md). Новый релиз не опубликован.

## Локальный кандидат 6.3.3 — актуальное поведение

Постоянные данные теперь в `%LOCALAPPDATA%\EXPC\Uninstaller\Data`. Описания записи рядом с EXE выше относятся к прежним версиям. Старые данные из папки запуска копируются с проверкой хеша, без удаления оригиналов и без перезаписи существующего профиля. Трей удалён; крестик закрывает приложение, активная операция защищена предупреждением. Самообновление не создаёт соседние файлы: для другого тома требуется ручная замена. См. [отчёт](docs/EXPC_USER_PROFILE_6.3.3.md).
