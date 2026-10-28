# EXPC backlog

Стадия: активное пользовательское тестирование и сбор багов. Функциональная разработка на паузе. Этот файл фиксирует отдельные будущие задачи, не разрешает их реализацию.

| Задача | Состояние / доказательства |
|---|---|
| Кэш и диагностические метрики в LocalAppData | Исправлено в локальном 6.3.2: Cache/Logs, метрики off по умолчанию; 115 тестов и console extraction. GUI NOT TESTED. |
| Данные профиля и трей | Исправлено в локальном 6.3.3: Data под LocalAppData, copy-only legacy import, tray удалён. Реальный перенос пользовательского vault и GUI-приёмка NOT TESTED. |
| Сортировка EN/RU | Отложено пользователем; воспроизведение и точный affected build UNKNOWN. |
| Сохранение прокрутки | Отложено пользователем; новое воспроизведение не выполнялось. |
| Улучшение окна остатков | Отложено пользователем; не смешивать с переносами каталогов. |
| PDF-XChange / реальное поведение остатков на ноутбуке | Исторические сообщения и отчёты сохранены; новая GUI/полевая приёмка NOT TESTED. |
| GUI, DPI, старт EXPCdelete.exe и реальный runtime/UAC | Ручная приёмка; фоновая проверка payload не заменяет её. |
| Неизвестные/уникальные остаточные материалы архива | Сохраняются; перечень в artifacts/verification/restructure-20261026/preserved-uncertain.json. |

Новые функциональные исправления — только по отдельному согласованному заданию. Список не утверждает, что исторические дефекты подтверждены в новом кандидате.

Выпуск 6.3.1: самообновление и жизненный цикл разрешены отдельно и реализованы. GUI/tray/focus, mixed-integrity/session и реальное самообновление опубликованного GUI остаются NOT TESTED; подробности EXPC_RELEASE_6.3.1.md. Старые известные дефекты удаления этим выпуском не закрываются.

## 2026-10-28 — окно самообновления остаётся на загрузке

- REPORTED: после нахождения новой версии и запуска обновления окно остаётся на «Загрузка и проверка…», видимого результата нет. Скриншот пользователя `2026-10-28_201942.jpg`. Точная запущенная версия и длительность ожидания UNKNOWN.
- EXPECTED: видимый прогресс/этап, завершение либо понятная ошибка; закрытие отменяет загрузку.
- AUDIT: HEAD 766bd206, codex/release-6.3.3. В SelfUpdateUi.Install нет прогресса; SelfUpdateClient.Fetch буферизует весь файл перед записью. Есть таймаут 90 секунд для HTTP и 3 минуты на Fetch с чтением тела. Это объясняет отсутствие обратной связи, но не доказывает причину конкретного зависания.
- BACKGROUND CHECK: production Check + Fetch + Verify, публичный релиз 6.3.3, 9 359 872 байта, SHA256 96b9d9f9f8ca01dbf58506ef69b22144edafe1e4d64ac43c5c169ef7baa1077e; manifest за 3,94 с, вся проверка за 6,40 с. Probe под build/selfupdate-download-audit, result.txt. Загруженный EXE не запускался; рабочий экземпляр не заменялся.
- Процесс EXPC при диагностике отсутствовал; привязка скриншота к версии не выполнена. LIVE GUI / полный цикл замены: NOT TESTED. Сбой не воспроизведён, не исправлен. Нужны версия исходного EXE и фактическое время ожидания/итог ошибки.
- Стадия остаётся сбором багов; исходники, dist, опубликованный release не изменены, новая публикация не выполнялась.

## 6.3.4 — authorized self-update follow-up

Implemented progress, bounded helper handoff, persistent stage journal, compact localized summary, target-volume replacement preparation and rollback guards. The original 6.3.2 GUI stall was not reproduced: its actual networking and helper passed isolated component checks. Historical cause remains UNKNOWN; GUI acceptance remains open. See EXPC_SELF_UPDATE_6.3.4.md. Real cross-volume replacement NOT TESTED. No removal/leftover fixes included.

6.3.5: version-directory observation and compact leftover outcome implemented/tested with synthetic fixtures; actual 1C uninstall and physical GUI/DPI NOT TESTED. Details in EXPC_LEFTOVERS_6.3.5.md. User explicitly approved publication.
