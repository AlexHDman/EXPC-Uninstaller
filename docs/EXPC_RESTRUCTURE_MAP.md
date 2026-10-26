# Карта реорганизации — 2026-10-26

Область: только EXPC-Uninstaller. Функциональные исправления не входят. База 71d8390e; пользовательские изменения сохранены. Модель/effort средствами сессии не подтверждены.

| Старый путь (от корня проекта) | Новый путь |
|---|---|
| `source` | `src` |
| `src/BulkCrapUninstallerTests` | `tests/BulkCrapUninstallerTests` |
| `src/ExpcSandboxTests` | `tests/ExpcSandboxTests` |
| `src/ExpcUninstallerTests` | `tests/ExpcUninstallerTests` |
| `src/ExpcVerification` | `tests/ExpcVerification` |
| `src/SimpleTreeMapTests` | `tests/SimpleTreeMapTests` |
| `tools/Expc.PackageProbe` | `src/Expc.PackageProbe` |
| `tools/Expc.PortableLauncher` | `src/Expc.PortableLauncher` |
| `tools/Expc.OperationFixture` | `tests/Expc.OperationFixture` |
| `doc` | `docs/upstream/manual` |
| `installer` | `docs/upstream/installer` |
| `bin` | `build/legacy-bin` |
| `artifacts/native` | `build/native` |
| `artifacts\acceptance-amber` | `artifacts\archive\legacy-20261026\acceptance-amber` |
| `artifacts\acceptance-final` | `artifacts\archive\legacy-20261026\acceptance-final` |
| `artifacts\acceptance-final-v2` | `artifacts\archive\legacy-20261026\acceptance-final-v2` |
| `artifacts\amber-height` | `artifacts\archive\legacy-20261026\amber-height` |
| `artifacts\completion` | `artifacts\archive\legacy-20261026\completion` |
| `artifacts\completion-candidate` | `artifacts\archive\legacy-20261026\completion-candidate` |
| `artifacts\confirmed-removal` | `artifacts\archive\legacy-20261026\confirmed-removal` |
| `artifacts\direct-removal` | `artifacts\archive\legacy-20261026\direct-removal` |
| `artifacts\field-candidate` | `artifacts\archive\legacy-20261026\field-candidate` |
| `artifacts\grouped-candidate` | `artifacts\archive\legacy-20261026\grouped-candidate` |
| `artifacts\phase01` | `artifacts\archive\legacy-20261026\phase01` |
| `artifacts\phase02` | `artifacts\archive\legacy-20261026\phase02` |
| `artifacts\phase02a` | `artifacts\archive\legacy-20261026\phase02a` |
| `artifacts\phase03` | `artifacts\archive\legacy-20261026\phase03` |
| `artifacts\phase03a` | `artifacts\archive\legacy-20261026\phase03a` |
| `artifacts\release-completion` | `artifacts\archive\legacy-20261026\release-completion` |
| `artifacts\selection-ruen` | `artifacts\archive\legacy-20261026\selection-ruen` |
| `artifacts\unified-operation` | `artifacts\archive\legacy-20261026\unified-operation` |
| `artifacts\update-selection` | `artifacts\archive\legacy-20261026\update-selection` |
| `artifacts\update-selection-final` | `artifacts\archive\legacy-20261026\update-selection-final` |
| `artifacts/archive/legacy-20261026/phase01/dotnet8` | `build/toolchain/dotnet8` |
| `artifacts/archive/legacy-20261026/phase01/mfc-x86` | `build/toolchain/mfc-x86` |
| `artifacts/archive/legacy-20261026/phase01/netfxsdk` | `build/toolchain/netfxsdk` |

Архив legacy сначала сохраняет контекст исторических доказательств. Воспроизводимые файлы будут отдельно перечислены и очищены по дополнительному разрешению; это не окончательная классификация мусора.
SDK/MFC/NETFX под build/toolchain — защищённое исключение, не удалять при очистке staging. Стандартные obj допустимы внутри проектов.
Неизвестные корневые %ProgramData% и desktop.ini остаются на месте. Пользовательские assets не меняются. Старые отчёты и исходные карты очистки не переписываются.
Два запущенных EXPC используют origin вне проекта. Их процессы, внешние файлы и кэши не затрагиваются.
Installer — только архив исходных Inno Setup скриптов upstream, без создания установщика.

## Итог переноса

Каталог grouped-candidate перенесён пофайлово после отказа Windows переименовать родителя: 118 файлов, ошибок нет; старый корень отсутствует. Все новые пути из карты действуют; часть воспроизводимых файлов затем удалена по отдельным CSV-манифестам дополнительного разрешения. Старый control EXE перенесён в artifacts/archive/control/71d8390e; его исходный SHA256 сохранён.

Старые README, publish.bat, ZIP/A+B build/verify-скрипты и workflows сохранены как материалы upstream в docs/upstream. Активные publish.bat, Build-ExpcPortable.proj и Build-ExpcSingleFile.ps1 направляют к одной команде Build-EXPCdelete.ps1. Старое имя Verify-ExpcSingleFile.py теперь выполняет только статическую проверку payload.

Source layout BCU и src/BulkCrapUninstaller.sln оставлены относительно друг друга; тестовые проекты вынесены в tests со ссылками ../../src. Два standalone helper-проекта из tools не наследуют лишние BCU shared sources — для этого оставлены локальные Directory.Build.props.
