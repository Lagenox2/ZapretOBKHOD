# ZapretOBKHOD — как устроена программа

Документ описывает обе версии: Windows (`src/`, `build.ps1`) и Android (`Android/`).
Он отвечает на два вопроса: **что происходит во время работы** и **за что отвечает каждый файл**.

---

## 1. Главная идея

ZapretOBKHOD — локальный обход DPI-блокировок. Он **не использует VPN-серверы**:
весь трафик идёт через обычного провайдера, а программа лишь незаметно меняет
форму первых пакетов соединения, чтобы фильтр провайдера не распознал запрещённый
ресурс и не оборвал соединение.

Что именно делается с трафиком:

| Приём | Суть |
|---|---|
| `split` / `multisplit` / `multidisorder` | TLS/HTTP-пакет режется на части; DPI видит только фрагменты и не находит в них имя сайта (SNI/Host). |
| `fake` | Перед настоящим пакетом отправляется поддельный пакет с «плохим» содержимым. DPI принимает его за начало соединения и запоминает неверное состояние, а настоящий пакет проходит. |
| `oob` (out-of-band) | Один байт отправляется отдельно, из-за чего меняется разбиение TCP-потока. |
| `tlsrec` | Меняется разбиение TLS-записей. |
| `--dpi-desync-fooling=badseq,md5sig` | Способы сделать фейковый пакет «неправильным» для DPI, но безвредным для получателя. |
| Блокировка QUIC (UDP/443) | Заставляет браузер откатиться с QUIC на обычный TCP/TLS, который проще обработать. |

Ключевое свойство: **ни один пакет не уходит на сторонний сервер**. Меняется только
локальная обработка исходящего трафика.

---

## 2. Windows-версия

### 2.1. Схема работы

```
ZapretOBKHOD.exe (GUI, C# WinForms)
        │  формирует аргументы командной строки
        ▼
engine\winws.exe  (движок zapret, GPL-3.0)
        │  перехватывает пакеты через драйвер WinDivert
        ▼
Сетевой стек Windows → провайдер → интернет
```

`winws.exe` — это `nfqws`-подобный движок проекта zapret, портированный под Windows.
Он работает в пространстве пользователя, но перехват пакетов выполняет драйвер
`WinDivert64.sys`, поэтому **нужны права администратора**.

### 2.2. Поток запуска

1. `Program.Main` проверяет аргумент `--autostart`, берёт мьютекс
   `ZapretOBKHOD_SingleInstance` (второй экземпляр не запускается).
2. Создаётся `MainForm`, поднимается трей, таймер обновления статуса.
3. Настройки читаются из `%LOCALAPPDATA%\ZapretOBKHOD\settings.json`.
   Если файл создан старой версией (нет поля `SettingsVersion`), при чтении
   включается блокировка QUIC и в журнал пишется сообщение об обновлении.
4. Пользователь выбирает профиль → `PresetCatalog.Build` собирает аргументы
   (`--wf-tcp`, `--filter-tcp`, `--hostlist`, `--dpi-desync=...`) и при необходимости
   готовит рабочие списки доменов в `%LOCALAPPDATA%\ZapretOBKHOD\active.txt`.
   Для профилей с общим HTTPS-профилем (`Fallback`) дополнительно добавляются
   правила без списка доменов — для Telegram и CDN без SNI.
5. `Engine.Start` запускает `winws.exe` скрыто, с перенаправлением stdout/stderr в лог.
6. Если включена блокировка QUIC — создаётся правило брандмауэра
   `ZapretOBKHOD_QUIC` (блок исходящего UDP/443).
7. Через 0,9 с проверяется, не завершился ли движок сразу; при ошибке показывается
   понятное сообщение (антивирус, конфликт с другим обходом и т. п.).
8. Пока движок жив, GUI показывает зелёный статус, PID и кнопку «Остановить».
9. При выходе/остановке правило брандмауэра удаляется, процесс убивается,
   «осиротевшие» `winws.exe` из этой же папки также добиваются.

### 2.3. Файлы Windows

#### `src/Program.cs`
Точка входа. Разбирает `--autostart`, обеспечивает единственный экземпляр,
ставит глобальные обработчики необработанных исключений (чтобы показывать ошибку,
а не молча падать), включает визуальные стили и запускает `MainForm`.

#### `src/AppCore.cs`
«Инфраструктура» приложения, четыре блока:

* **`AppPaths`** — все пути: папка программы, `engine\winws.exe`,
  `engine\files\fake`, `lists`, `%LOCALAPPDATA%\ZapretOBKHOD` (`settings.json`,
  `active.txt`, `active-exclude.txt`, `logs`). `ToEnginePath` приводит путь
  к виду, который понимает движок.
* **`AppSettings`** — модель настроек: `PresetId`, `Autostart`, `MinimizeToTray`,
  `BlockQuic`, `ExcludeRu`, `AutoRestart`, `SettingsVersion`. Чтение/запись JSON через
  `JavaScriptSerializer`; при повреждённом файле возвращаются значения по умолчанию.
  `BlockQuic` по умолчанию `true`; `SettingsVersion` нужен для мягкой миграции
  настроек старых версий без сброса выбора пользователя.
* **`Logger`** — запись в `logs\zapretobkhod-ГГГГММДД.log`, ротация при размере
  больше 3 МБ, событие `Message` для живой ленты в окне программы.
* **`Admin` и `ProcessRunner`** — проверка прав администратора и запуск внешних
  процессов (`schtasks.exe`, `netsh.exe`) с захватом вывода и таймаутом.

#### `src/Engine.cs`
Жизненный цикл движка:

* `Start` — проверяет наличие `winws.exe` и права администратора, при необходимости
  создаёт правило блокировки QUIC, запускает процесс скрыто, читает его вывод,
  ждёт 0,9 с и проверяет, что он не упал.
* `Stop` / `StopInternal` — снимает правило, убивает процесс, отписывается от событий,
  вызывает `KillStaleEngine`.
* `OnExited` — если процесс завершился не по нашей команде, поднимает событие
  `ExitedUnexpected` (GUI может показать ошибку и перезапустить).
* `KillStaleEngine` — ищет все `winws.exe`, сравнивает полный путь с нашим и убивает
  только свои «зависшие» копии.
* `AddQuicRule` / `RemoveQuicRule` — `netsh advfirewall` правило
  `ZapretOBKHOD_QUIC`, блокирующее исходящий UDP/443.

#### `src/Presets.cs`
Каталог профилей и **сборщик аргументов** движка.

`Preset` содержит: `Id`, `Name`, `Description`, `Lists` (файлы доменов),
`AllSites` (весь трафик), `Soft` (только разделение), `Voice` (добавить UDP-диапазон
для голоса Discord), `Fallback` (добавить общий HTTPS/QUIC-профиль к точечному).

`PresetCatalog.Build`:

1. Если профиль не «все сайты» и не «мягкий» — читает списки, убирает дубликаты
   и комментарии, пишет объединённый `active.txt`.
2. Если включено «исключать российские сервисы» — готовит `active-exclude.txt`
   из `lists\exclude.txt`.
3. Собирает профили в таком порядке (первый подходящий профиль выигрывает):
   * точечный TCP/80 — `fake,multisplit`, позиция `method+2`, `fooling=md5sig`;
   * точечный TCP/443 — `fake,multidisorder`, позиции `1,midsld`, `fooling=md5sig`,
     `fake-tls-mod=rnd,dupsid,sni=www.google.com`;
   * неизвестный TCP/443 — `filter-l7=unknown`, `fake,multisplit`, позиция `5`,
     `any-protocol`, `cutoff=n6`. Сюда попадает MTProto Telegram. Список исключений
     здесь не используется: профиль с одним `--hostlist-exclude` не срабатывает,
     когда имени хоста нет. Профиль идёт **перед** общими, чтобы работать и при
     выключенной галочке исключений;
   * общий TCP/80 — `fake,fakedsplit`, `autottl=2` (стратегия из официального
     `zapret-win-bundle`, preset1);
   * общий TCP/443 — `fake,multidisorder`, позиция `midsld`, `fooling=badseq,md5sig`;
   * общий UDP/443 + `--filter-l7=quic` — `fake` с повторами;
   * при `Voice` — UDP `50000-50099,19294-19344` с фейковым Discord-пакетом.
4. Добавляет `--wf-tcp=80,443 --wf-udp=443` (для голоса — ещё два UDP-диапазона),
   `--wf-filter-lan=1` (не трогать локальную сеть); мягкий режим — только
   `--wf-tcp=443` и профиль `multisplit` без подмены.

Важно: `--new` разделяет независимые профили внутри одного запуска `winws.exe`.
Общие профили (TCP/80, TCP/443, QUIC) добавляются для `AllSites` и для профилей
с `Fallback`; точечные профили со списком доменов идут раньше общих.

#### `src/MainForm.cs`
Вся логика окна (911 строк):

* построение интерфейса: карточки статуса, выпадающий список профилей, чекбоксы
  «Автозапуск», «Сворачивать в трей», «Блокировать QUIC», «Исключать российские
  сервисы», кнопки «Включить/Остановить», «Проверить доступ», «Журнал», «О программе»;
* `StartEngine` / `StopEngine` — запуск и остановка с обработкой ошибок;
* `PresetChanged` + `ApplyLiveChange` — смена профиля или настроек на лету
  (перезапуск движка без перезапуска программы);
* `UpdateUi` — раз в секунду обновляет статус, PID, доступность кнопок;
* `SetupTray` — иконка в трее, меню «Показать», «Включить/Остановить»,
  «Проверить доступ», «Выход»; закрытие окна при включённой опции прячет его в трей;
* `TestAccess` — асинхронная проверка сайтов через `NetTest`;
* `OnLogMessage` / `AppendLog` — живая лента событий;
* `OnFormClosing` — аккуратная остановка движка и сохранение настроек.

#### `src/NetTest.cs`
Проверка доступности: `YouTube`, `Discord`, `Telegram` (HTTPS-запрос с таймаутом).
Возвращает `CheckResult` (имя, успех, предупреждение, миллисекунды, текст ошибки).
Запускается в фоновом потоке, результат отдаётся через callback.

#### `src/AutostartTask.cs`
Автозапуск через Планировщик заданий Windows (не через папку «Автозагрузка» —
так можно запускать с повышенными правами без запроса UAC при каждом входе).
Создаёт задачу `ZapretOBKHOD` с аргументом `--autostart`, проверяет и удаляет её
через `schtasks.exe`. XML задачи формируется в коде.

#### `src/UiControls.cs`
Тема и собственные контролы: палитра (`Theme`), скруглённая панель `RoundedPanel`,
кнопка `RoundedButton`, индикатор `StatusDot`, переключатель. Нужны, потому что
стандартные WinForms-контролы выглядят устаревше.

#### `src/AssemblyInfo.cs`
Метаданные сборки: название, версия, автор, copyright.

#### `src/app.manifest`
Манифест приложения: `requestedExecutionLevel level="requireAdministrator"`,
DPI-awareness, поддержка Windows 10/11. Именно он вызывает UAC при старте.

#### `build.ps1`
Сборка без Visual Studio:

1. создаёт папки `assets` и `dist`;
2. если нет `assets\app.ico` — рисует иконку-щит через `System.Drawing`;
3. компилирует все `src\*.cs` компилятором `csc.exe` из .NET Framework
   (`/target:winexe`, `/win32manifest`, `/win32icon`);
4. копирует в `dist` exe, `engine`, `lists`, лицензии и README;
5. если не указан `-NoZip` — пакует `dist` в `ZapretOBKHOD-portable.zip`.

#### Данные Windows
* `lists\youtube.txt`, `discord.txt`, `telegram.txt`, `media.txt`, `custom.txt` —
  домены для профилей (по одному на строку, `#` — комментарий).
* `lists\exclude.txt` — банки, госуслуги, российские сервисы, которые нельзя ломать.
* `engine\winws.exe`, `WinDivert.dll`, `WinDivert64.sys`, `cygwin1.dll` — движок.
* `engine\files\fake\*.bin` — эталонные пакеты для `fake`/QUIC/Discord.
* `engine\LICENSE-*`, `THIRD-PARTY-NOTICES.txt` — лицензии (zapret GPL-3.0,
  WinDivert LGPL-3.0 и т. д.).

---

## 3. Android-версия

### 3.1. Основа

Android-версия — форк **ByeByeDPI 1.8.2** (GPL-3.0). Используются два нативных
компонента:

* **ByeDPI** (`libbyedpi.so`) — тот же класс десинхронизации, что и zapret,
  но в виде библиотеки с JNI;
* **hev-socks5-tunnel** (`libhev-socks5-tunnel.so`) — поднимает TUN-интерфейс
  и заворачивает трафик в локальный SOCKS5-прокси ByeDPI.

`namespace` оставлен `io.github.romanvht.byedpi`, потому что от него зависят имена
JNI-функций; `applicationId` — `ru.zapretobkhod.android`.

### 3.2. Схема работы

```
Приложения Android
        │  весь трафик (VpnService)
        ▼
ByeDpiVpnService (отдельный процесс :native)
        │  создаёт TUN, передаёт fd в hev-socks5-tunnel
        ▼
libhev-socks5-tunnel.so ──SOCKS5──► ByeDPI (127.0.0.1:1080)
                                          │  split/fake/oob/tlsrec
                                          ▼
                                   обычный сокет → провайдер → интернет
```

* `VpnService` **не отправляет трафик на удалённый сервер** — он лишь забирает
  пакеты у системы и отдаёт их локальному движку.
* ByeDPI слушает `127.0.0.1:1080` (SOCKS5) и применяет стратегию к каждому
  соединению.
* hev-socks5-tunnel соединяет TUN и SOCKS5.
* Всё работает без root.

### 3.3. Поток запуска

1. `ZapretApp.onCreate` — при первом запуске синхронизирует встроенные списки
   доменов и добавляет 8 профилей ZapretOBKHOD в закреплённую историю стратегий,
   делает «Комбо» стратегией по умолчанию.
2. `MainActivity` показывает кнопку подключения, выбранную стратегию и статус.
3. Нажатие → `VpnService.prepare()` (системный диалог разрешения VPN) →
   `ServiceManager.startService`.
4. `ServiceManager` запускает `ByeDpiVpnService` (режим VPN) или
   `ByeDpiProxyService` (режим прокси) в процессе `:native`.
5. `NativeSession` через JNI запускает ByeDPI, получает порт, поднимает
   hev-socks5-tunnel на файловом дескрипторе TUN.
6. `ByeDpiVpnService` переводит себя в foreground с постоянным уведомлением
   (кнопки «Пауза» и «Стоп»).
7. Состояние (`AppStatus`) и режим (`Mode`) рассылаются широковещательными
   интентами; UI, плитка быстрых настроек и уведомление обновляются.
8. При выходе из приложения VPN продолжает работать; остановка — кнопкой,
   из уведомления, плитки или `ToggleActivity`.

### 3.4. Файлы Android

#### Корень пакета

| Файл | Роль |
|---|---|
| `ZapretApp.kt` | `Application`: первичное заполнение профилей и списков, версия «посева». |
| `ZapretPresets.kt` | 8 готовых профилей (`BASE = -o1 -a1 -r-5+se`) и ключ `zapretobkhod_presets_seeded`. |

#### `activities/`

| Файл | Роль |
|---|---|
| `BaseActivity.kt` | Базовое activity: общая инициализация темы/языка. |
| `MainActivity.kt` | Главный экран: статус, большая кнопка подключения, выбор стратегии, меню, запрос разрешений VPN/уведомлений, запуск теста. |
| `ToggleActivity.kt` | Невидимое activity для виджета/ярлыка/плитки: включает или выключает VPN одним действием. |
| `SettingsActivity.kt` | Контейнер экранов настроек, импорт/экспорт настроек. |
| `TestActivity.kt` | Экран автоподбора стратегий: прогресс, список результатов, применение найденной стратегии. |
| `TestSettingsActivity.kt` | Настройки теста (какие списки сайтов проверять). |
| `FileActivity.kt` | Встроенный текстовый редактор файлов (списки доменов, конфиги). |

#### `fragments/`

| Файл | Роль |
|---|---|
| `MainSettingsFragment.kt` | Основные настройки: режим, автозапуск, уведомления, язык, поведение. |
| `ByeDpiCMDSettingsFragment.kt` | Ручной ввод аргументов ByeDPI, история, закрепление стратегий. |
| `ByeDpiUISettingsFragment.kt` | Настройки стратегии через удобные переключатели (метод, позиция split, TTL, fake SNI и т. д.). |
| `AppSelectionFragment.kt` | Выбор приложений, которые идут через VPN (или исключены). |
| `DomainListsFragment.kt` | Управление списками доменов: включение, редактирование, обновление. |
| `ProxyTestSettingsFragment.kt` | Параметры автоподбора: количество запросов, таймаут, параллельность. |

#### `services/`

| Файл | Роль |
|---|---|
| `ServiceManager.kt` | Оркестратор: старт/стоп/пауза, защита от гонок (`Mutex`), рассылка состояния, восстановление после сбоя. |
| `NativeEngine.kt` | Управление нативным процессом через `Messenger`/`AIDL`-подобный протокол, JNI-вызовы, слежение за PID. |
| `NativeSession.kt` | Одна сессия: запуск ByeDPI, старт hev-socks5-tunnel на fd, остановка, таймауты. |
| `ByeDpiVpnService.kt` | `VpnService`: `Builder` (адрес, DNS, маршруты, разрешённые приложения), foreground-уведомление. |
| `ByeDpiProxyService.kt` | Режим «только прокси» без VPN: ByeDPI слушает локальный порт, приложения настраиваются вручную. |
| `TestService.kt` | Фоновый автоподбор стратегий: перебирает команды, проверяет сайты, сохраняет результаты. |
| `QuickTileService.kt` | Плитка в шторке быстрых настроек. |
| `EngineProtocol.kt` | Константы протокола обмена между UI и нативным процессом. |
| `ByeDpiStatus.kt` | Глобальная переменная `appStatus` (статус + режим). |

#### `receiver/`

| Файл | Роль |
|---|---|
| `BootReceiver.kt` | Автозапуск после перезагрузки, если включён `autostart`. |
| `ServiceActionReceiver.kt` | Обрабатывает действия из уведомлений: `START`, `STOP`, `PAUSE`, `RESUME`, `RESTART`. |

#### `core/`

| Файл | Роль |
|---|---|
| `ByeDpiProxy.kt` | JNI-обёртка: `System.loadLibrary("byedpi")`, `startProxy(args)`, `stopProxy()`. |
| `TProxyService.kt` | JNI-обёртка hev-socks5-tunnel: `startTunnel(config, fd)`, `stopTunnel()`. |
| `ByeDpiProxyPreferences.kt` | Преобразование настроек UI и командной строки в массив аргументов ByeDPI; разбор `-i`/`-p` (IP/порт), проверка конфликтов. |

#### `data/`

| Файл | Роль |
|---|---|
| `Actions.kt` | Строковые константы действий (`START_ACTION` и др.). |
| `Broadcasts.kt` | Имена широковещательных интентов для статуса. |
| `AppStatus.kt` | Перечисление состояний (`Halted`, `Starting`, `Running`, `Stopping`). |
| `Command.kt` | Элемент истории: текст команды, имя, флаг закрепления. |
| `Configuration.kt` | Готовая конфигурация: аргументы, host, port. |
| `DomainList.kt` | Модель списка доменов (id, имя, домены, встроенный/пользовательский). |
| `AppInfo.kt` | Имя/пакет/иконка приложения для выбора. |
| `AppSettings.kt` | Модель импорта/экспорта настроек. |
| `PrivateDnsState.kt` | Состояние Private DNS (выключен/настроен/неизвестно). |
| `TestResult.kt` | Результат проверки одной стратегии. |
| `TestSettings.kt` | Параметры теста. |
| `TestState.kt` | Текущее состояние теста. |
| `UISettings.kt` | Все параметры «удобной» стратегии: IP, порт, метод, split, TTL, fake SNI, режим хостов и т. д. |

#### `adapters/`

| Файл | Роль |
|---|---|
| `AppSelectionAdapter.kt` | Список приложений с чекбоксами. |
| `DomainListAdapter.kt` | Список доменных списков. |
| `SiteResultAdapter.kt` | Результаты проверки сайтов. |
| `StrategyResultAdapter.kt` | Результаты автоподбора стратегий. |

#### `utility/`

| Файл | Роль |
|---|---|
| `PreferencesUtils.kt` | Обёртки над `SharedPreferences`, значения по умолчанию (`-o1 -a1 -r-5+se`), ключи настроек. |
| `HistoryUtils.kt` | История команд: добавление, закрепление, ограничение 40 записями. |
| `DomainListUtils.kt` | Синхронизация встроенных списков из assets, CRUD пользовательских, раскрытие `{list:...}` в домены. |
| `SettingsUtils.kt` | Импорт/экспорт настроек в JSON, смена языка, сброс, выбор разделов. |
| `DiagnosticUtils.kt` | Текст диагностического отчёта (версия, режим, стратегия, DNS, списки) для отправки разработчику. |
| `NotificationUtils.kt` | Каналы уведомлений, foreground-уведомление с кнопками. |
| `SiteCheckUtils.kt` | Проверка сайтов через SOCKS5-прокси: несколько попыток, контроль длины ответа (детект «заглушки»). |
| `ArgumentsUtils.kt` | `shellSplit` — разбор строки аргументов с кавычками (сохраняет `-H:"a b c"` одним токеном). |
| `ValidateUtils.kt` | Валидация IP, домена, порта в полях настроек. |
| `PermissionUtils.kt` | Разрешения: уведомления, VPN, отключение оптимизации батареи, доступ к файлам. |
| `PrivateDnsUtils.kt` | Чтение состояния Private DNS и переход в системные настройки. |
| `StorageUtils.kt` | Поиск корней внешнего хранилища и проверка, что файл внутри разрешённой папки. |
| `ShortcutUtils.kt` | Динамические ярлыки: «Включить/выключить» и до трёх закреплённых стратегий. |
| `ClipboardUtils.kt` | Копирование/вставка команд и отчётов. |
| `ConfigurationUtils.kt` | Собирает `Configuration` для теста из команды, host и port. |

#### Ресурсы

* `res/layout/*` — экраны и элементы списков (главный экран, настройки, тест,
  редактор файлов, выбор приложений, история, списки доменов).
* `res/xml/*.xml` — экраны `PreferenceFragmentCompat`: `main_settings.xml`,
  `byedpi_cmd_settings.xml`, `byedpi_ui_settings.xml`, `proxy_test_settings.xml`.
* `res/values/strings.xml` — русские строки; `values-en`, `values-kk`, `values-tr`,
  `values-vi` — переводы; `arrays.xml` — варианты выбора.
* `res/values/themes.xml`, `values-night`, `values-v31` — тема, тёмный режим.
* `res/drawable/*` — иконки, включая щит ZapretOBKHOD и `ic_notification`.
* `res/mipmap-*` — иконки приложения.
* `assets/proxytest_*.sites` — встроенные списки доменов (youtube, googlevideo,
  discord, telegram, social, cloudflare, general, turkiye, custom).
* `assets/proxytest_strategies.list` — набор стратегий для автоподбора.

#### Нативные библиотеки

`app/src/main/jniLibs/<ABI>/libbyedpi.so` и `libhev-socks5-tunnel.so` для
`arm64-v8a`, `armeabi-v7a`, `x86`, `x86_64`. Взяты из официального universal APK
ByeByeDPI 1.8.2; NDK при сборке не требуется.

#### Конфигурация сборки

* `Android/app/build.gradle.kts` — `compileSdk 36`, `minSdk 21`, `targetSdk 34`,
  `applicationId ru.zapretobkhod.android`, `versionName 1.0.0`, ViewBinding,
  минификация release, необязательная подпись из `keystore.properties`.
* `Android/app/proguard-rules.pro` — правила ProGuard; DTO Gson сохраняются,
  классы переупаковываются в `ru.zapretobkhod`.
* `Android/build.gradle.kts`, `settings.gradle.kts`, `gradle.properties` —
  стандартная конфигурация Gradle.
* `Android/AndroidManifest.xml` (в `app/src/main`) — разрешения, сервисы
  в процессе `:native`, ресиверы, плитка, `android:name=".ZapretApp"`.
* `Android/build-release.ps1` — сборка подписанного release APK.
* `Android/run-tests.ps1` — прогон unit-тестов через зеркальную копию
  (обходит ошибку Kotlin при пути с `!`).
* `Android/keystore.properties`, `Android/keystore/zapretobkhod.jks` — ключ подписи.

---

## 4. Соответствие профилей

| Windows (`PresetCatalog`) | Android (`ZapretPresets`) | Область |
|---|---|---|
| Комбо: YouTube + Discord + Telegram | Комбо: YouTube + Discord + Telegram | списки youtube/discord/telegram |
| Только YouTube | Только YouTube | youtube + googlevideo |
| Только Discord | Только Discord | discord |
| Telegram + весь HTTPS | Telegram + весь HTTPS | весь HTTPS (`-Kt`) |
| Соцсети и медиа | Соцсети и медиа | media / social+discord |
| Все сайты (агрессивно) | Все сайты (агрессивно) | весь трафик |
| Мягкий режим | Мягкий режим | только split |
| Свой список сайтов | Свой список сайтов | `custom` |

Разница реализации: Windows передаёт домены файлом (`--hostlist`), Android —
строкой в аргументе (`-H:"{list:...}"`), которую `DomainListUtils` раскрывает
в перечисление доменов. В обоих случаях действует список исключений.

Роутерная версия (`Router/presets.sh`) использует те же 8 профилей: для `tpws`
это `--filter-tcp` + `--split-pos`/`--disorder`, для `nfqws` — те же
`--dpi-desync`-стратегии, что и в Windows. QUIC обрабатывается правилом
iptables, а не фейковым UDP-пакетом, когда выбран `tpws`.

---

## 5. Сборка и запуск

Windows:

```powershell
.\build.ps1            # dist\ZapretOBKHOD.exe + engine + lists + portable zip
```

Android (debug):

```powershell
cd Android
$env:JAVA_HOME='C:\dev\jdk\jdk-21.0.1'
.\gradlew.bat assembleDebug --no-daemon
```

Android (release):

```powershell
cd Android
powershell -ExecutionPolicy Bypass -File .\build-release.ps1
# результат: Android\dist\ZapretOBKHOD.apk
```

Тесты:

```powershell
cd Android
powershell -ExecutionPolicy Bypass -File .\run-tests.ps1
```

Роутер (ASUS RT-N16 / BusyBox):

```powershell
powershell -ExecutionPolicy Bypass -File .\build-router.ps1
# результат: dist\ZapretOBKHOD-router.zip
# с -Download — повторно скачать бинарники zapret v72.13
```

---

## 6. Ограничения

* Windows: нужны права администратора (WinDivert); антивирус может блокировать
  драйвер; одновременно должен работать только один обход (zapret/GoodbyeDPI).
* Android: нужен `VpnService` (системный диалог); на некоторых прошивках
  агрессивное энергосбережение убивает службу — помогает отключение оптимизации
  батареи; Private DNS может мешать, если провайдер блокирует по DNS.
* Ни одна версия не гарантирует обход при смене стратегии блокировок: профили
  подобраны как универсальные, при необходимости стратегию можно менять вручную.
* Трафик не шифруется дополнительно и не проходит через посредников — это
  принципиальное свойство проекта.
* Роутер: `tpws` не работает с UDP (QUIC просто блокируется, голос Discord не
  обходится); `nfqws` требует NFQUEUE. На живом RT-N16 версия не тестировалась —
  доступен только статический контроль (`Router/check.sh`).

---

## 7. Лицензии

* Android-форк: **GPL-3.0** (наследуется от ByeByeDPI).
* ByeDPI: **MIT**.
* hev-socks5-tunnel: **MIT**.
* Windows-движок zapret: **GPL-3.0**; WinDivert: **LGPL-3.0**.
* Роутерные бинарники `tpws`/`nfqws`: **GPL-3.0** (zapret v72.13).
* Полные тексты — в `Android/THIRD-PARTY-NOTICES.txt`, `engine/LICENSE-*`
  и `Router/LICENSE-*`.

---

## 8. Версия для роутера (BusyBox)

```
Router/
  install.sh      копирование в /jffs|/opt|/etc, chmod, автозапуск
  zapret.sh       start/stop/restart/status/preset, PID, iptables, web
  config.sh       режим, порт, LAN/WAN, QUIC, EXCLUDE_RU, web
  presets.sh      сборка аргументов tpws/nfqws под 8 профилей
  check.sh        диагностика окружения и бинарников
  bin/            tpws/nfqws для mipsel и mips (статические, musl)
  lists/          те же списки, что в Windows-версии
  fake/           QUIC Initial и Discord voice для nfqws
  web/            BusyBox httpd + CGI на русском
```

Два режима:

* `tpws` — прозрачный TCP-прокси. `iptables -t nat PREROUTING` на LAN
  перенаправляет TCP 80/443 на `0.0.0.0:988`; `tpws` меняет пакеты и
  соединяется с сайтом напрямую. Не требует NFQUEUE, работает на ядрах 2.6.
* `nfqws` — NFQUEUE-движок с полным набором `--dpi-desync`, включая
  fake-пакеты, QUIC и голос Discord.

Правила создаются только для LAN-интерфейса, только на время работы и
снимаются командой `stop` (а также при неудачном старте). Для `nfqws`
используется `--queue-bypass`: падение движка не блокирует трафик.
Веб-интерфейс — CGI с whitelist действий, без произвольных команд из URL.

Автозапуск: `jffs2_exec` (Tomato/ASUS), `rc_startup` (DD-WRT), symlink в
`/etc/rc.d` (OpenWrt), `/opt/etc/init.d` (Entware). Старое значение
сохраняется в `autostart.old`, `zapret.sh autostart-off` возвращает его.

