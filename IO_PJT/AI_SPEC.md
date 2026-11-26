# AI_SPEC — рабочее ТЗ проекта IO_PJT для ИИ-разработчиков

**Назначение файла:** этот документ — входная точка для любой ИИ-системы (Cline, Copilot, Claude и т.п.), которой поручена доработка проекта IO_PJT. Он отвечает на вопросы «что за проект, где что лежит, как собирать, как добавлять новую функцию, каких правил придерживаться, какие есть ловушки» — чтобы ИИ не тратил время на повторное исследование и не нарушал сложившиеся конвенции.

**Как пользоваться:**
1. Прочитать этот файл целиком перед первой задачей.
2. Уточнить детали в файлах из §11 «Карта документации» (ТЗ.md — поведение, IOclass.md — модель данных и журнал фич, SCADA_XML_STRUCTURE.md — формат XML).
3. Перед правкой кода прочитать целевые файлы целиком (или их значимые фрагменты) — никогда не править «по памяти».
4. После изменения пройти §13 «Чек-лист новой фичи» и §14 «Приёмка».

**Актуальность:** версия программы **1.1** (`AppInfo.Version`), смоук **120 OK / 0 FAIL** (11 тестов), последний коммит ветки `main` — `abd01a2`.

---

## 1. Обзор проекта

- **IO_PJT** (рабочее имя «IOListtoDB») — настольное Windows-приложение **WPF / .NET 8** (`net8.0-windows`, `<UseWPF>true</UseWPF>`, `Nullable=enable`, `ImplicitUsings=enable`).
- Назначение: импорт таблиц сигналов АСУ ТП (IO List) произвольной структуры из Excel в **Microsoft Access** (`.accdb` через ACE OLE DB), просмотр/редактирование/валидация, импорт карты адресации Modbus, связывание сигналов с картой (заполнение `M_*`-полей) и **генерация файлов для SCADA**:
  1. перечень «Тех.объекты» (`.xlsx`),
  2. **XML-блоки опроса Modbus** (`BufScadaPOUS`) — основной и самый сложный генератор,
  3. XML-экземпляры типовых алгоритмов (`BufScadaPOUS`).
- **Пакеты NuGet:** только `ClosedXML 0.104.1` (Excel) и `System.Data.OleDb 8.0.0` (Access). Новых зависимостей без явной необходимости не добавлять.
- **Язык интерфейса и журнала — русский.** Все сообщения пользователю, заголовки колонок, тултипы — по-русски. Код — идентификаторы на английском.
- Справочники (`Location`, `System`, `ExType`, типы I/O и т.д.) хранятся **строками; enum в модели данных не используется** (осознанное решение — хранение в Access TEXT).
- Исходники — UTF-8 (в большинстве файлов с BOM). Кириллица в строках, регулярках, путях — норма.

## 2. Быстрый старт (команды)

Все пути — из корня репозитория `Work\` (рабочая папка `c:\Users\RAN-Notebook\Desktop\Work`).

```powershell
# Сборка решения (ВАЖНО: .sln лежит в подпапке IO_PJT, не в корне!)
dotnet build "c:\Users\RAN-Notebook\Desktop\Work\IO_PJT\IO_PJT.sln" -c Debug
dotnet build "c:\Users\RAN-Notebook\Desktop\Work\IO_PJT\IO_PJT.sln" -c Release

# Смоук-тесты: СНАЧАЛА собрать решение, ПОТОМ запускать DLL напрямую
dotnet "c:\Users\RAN-Notebook\Desktop\Work\IO_PJT.Smoke\bin\Debug\net8.0-windows\IO_PJT.Smoke.dll"
# Код выхода 0 = все OK, 1 = есть FAIL. Итоговая строка: "ИТОГО: OK 120, FAIL 0".
```

**Ловушки запуска (обязательно к соблюдению):**
- **Не использовать `dotnet run` для смоука** — он пересобирает и может запустить устаревшую копию. Только `dotnet <путь-к-DLL>`.
- TFM смоука — тоже `net8.0-windows` (путь выше).
- Результаты сборки/тестов удобно писать в `%TEMP%`: `... > "$env:TEMP\build.log" 2>&1; Get-Content "$env:TEMP\build.log" -Tail 5`.
- При ошибках сборки — читать ПЕРВЫЕ строки лога (MSBuild пишет ошибки сверху).

## 3. Структура репозитория

```
Work\                                ← корень git-репозитория (origin: github.com/ahtoh80-hub/Work)
├── IO_PJT\                          ← основной WPF-проект
│   ├── IO_PJT.sln                   ← РЕШЕНИЕ (сюда добавлять проекты, напр. смоук)
│   ├── IO_PJT.csproj                ← net8.0-windows, WPF, 2 NuGet-пакета
│   ├── AppInfo.cs                   ← AppInfo.Version (сейчас "1.1")
│   ├── IOclass.md                   ← ГЛАВНЫЙ справочник: дерево IOPoint, DDL всех таблиц,
│   │                                   правила записи, ЖУРНАЛ фич (разделы F34…F105)
│   ├── SCADA_XML_STRUCTURE.md       ← полная расшифровка XML SCADA (BufScada/BufScadaPOUS)
│   ├── Генерация технологических объектов Modbus.md  ← спецификация колонок «Тех.объекты»
│   ├── Modbus RTU_TCP block.md      ← заметки по блокам Modbus RTU/TCP
│   ├── IOLISTtoDB_v0.MD             ← архивная история ТЗ (v0)
│   ├── _Specification\              ← исходные задания/скриншоты/примеры (read-only)
│   ├── Data\
│   │   ├── Schema.cs                ← ИМЕНА ВСЕХ ТАБЛИЦ БД (константы + DDL-шаблоны)
│   │   ├── FieldRegistry.cs         ← реестр колонок IO_LIST (алиасы, типы, обязательность)
│   │   └── FieldDescriptions.cs     ← описания полей для UI
│   ├── Models\
│   │   ├── IoPoint.cs               ← дерево IOPoint (IOclass) + M_-группа Modbus + Typno
│   │   ├── ModbusBlockPlan.cs       ← планы блоков + ScadaMbGenSettings (настройки F13)
│   │   ├── ModbusMapEntry.cs        ← строка карты MODBUS_MAP
│   │   ├── ModbusLinkRule.cs        ← правило связывания (маски)
│   │   ├── AlgoGenPlan.cs           ← планы генерации типовых алгоритмов (F65)
│   │   └── … (ColumnMapping, AuditRecord, ValidationReport, UpsertResult, ExcelRowData…)
│   ├── Services\                    ← ВЕСЬ БИЗНЕС-КОД (см. §4)
│   ├── ViewModels\                  ← MVVM: ViewModelBase, RelayCommand, VM всех окон
│   ├── Views\                       ← XAML окон (+ code-behind)
│   └── Resources\Styles.xaml        ← общие стили
├── IO_PJT.Smoke\                    ← смоук-тесты (консольный проект, ссылка на IO_PJT)
│   ├── IO_PJT.Smoke.csproj
│   ├── Program.cs                   ← partial-класс: Main, Check/Log/RunTest, помощники БД
│   ├── Tests.Db.cs                  ← тесты 1–2 (схема, upsert)
│   ├── Test3.MapImport.cs           ← тест 3 (импорт карты)
│   ├── Test4.Masks.cs               ← тест 4 (маски)
│   ├── Test5.Link.cs                ← тест 5 (связывание)
│   ├── Test6.Plan.cs                ← тест 6 (план блоков)
│   ├── Test7.Xml.cs                 ← тест 7 (генерация XML)
│   ├── Test8.MasterObjects.cs       ← тест 8 (объекты мастер-шаблона F101)
│   ├── Test9.RealTemplate.cs        ← тест 9 (реальный шаблон ModbusTCP_reserv_v0.xml)
│   ├── Test10.MasterObjectsBug.cs   ← тест 10 (баг ..._MAIN.BUSY.Busy)
│   ├── Test10.1_MasterObjectsBugSimple.cs ← тест 10.1 (юнит-проверки маппинга)
│   ├── Test11.UnboundBits.cs        ← тест 11 (F105 удаление непривязанных битов)
│   └── Gen.Common.cs                ← общие генераторы данных для тестов (xlsx через ClosedXML)
└── Work\                            ← рабочие данные пользователя (НЕ код)
    ├── ТЗ.md                        ← утверждённое ТЗ (ревизии, только реализованное)
    ├── _Шаблоны\*.xml               ← примеры XML-шаблонов SCADA
    └── … (Excel/Access файлы проекта)
```

## 4. Архитектура

### 4.1 Слои

```
Views (XAML + code-behind)          ← только визуал; code-behind минимальный (конструктор, DataContext)
   ↓ Binding / ICommand
ViewModels (ViewModelBase + RelayCommand)  ← состояние окна, команды, валидация UI, вызовы сервисов
   ↓ прямые вызовы (без DI-контейнера)
Services (sealed-классы)            ← ВЕСЬ бизнес-логика, работа с OleDb/Excel/XML
   ↓
Data (Schema, FieldRegistry) + Models (IoPoint и др.)
```

- DI-контейнера НЕТ: сервисы создаются вручную. Агрегат — **`AppServices`** (`Services/AppServices.cs`): один объект на главное окно, содержит `Log` (EventLogger), `Creator` (TableCreator), `Audit`, `Mapping`, `Repo` (IOListRepository), `Import`, `ModbusMaps`, `ModbusLink`, `ModbusLinkRules`, `ScadaTechObjects`, `ModbusBlocks` (ModbusBlockPlannerService), `ModbusBlocksXml` (ScadaTemplateXmlService), `AlgoTemplates`, `AlgoXml`. Переключение БД/таблицы — `UseDatabase(path)` / `UseTable(name)` (пересоздаёт сервисы + `ServiceSchema.EnsureServiceTables`).
- Окна SCADA (F13/F65/F12) создают **свои** экземпляры сервисов от текущего `DbPath`, не проходя через `AppServices`.

### 4.2 MVVM-конвенции

- Базовый класс — **`ViewModelBase`** (`ViewModels/RelayCommand.cs`): `Raise(name)` и `Set<T>(ref field, value, name)` (возвращает false, если значение не изменилось).
- Команды — **`RelayCommand`** / **`RelayCommand<T>`** (делегат + предикат, `CommandManager.RequerySuggested`).
- Свойства VM — стандартный INPC-паттерн через `Set(ref _x, value, nameof(X))`.
- Привязки в XAML — `Mode=TwoWay` для редактируемых, `OneWayToSource` для обратной записи. Свойства модели настроек должны быть INPC (иначе чекбокс не отработает).

### 4.3 Сервисы (карта ответственности)

| Сервис | Файл | Роль |
|---|---|---|
| `TableCreator` | TableCreator.cs | DDL: создание БД/таблиц/колонок, `EnsureServiceTables`, `EnsureColumns` (ALTER ADD) |
| `ServiceSchema` | ServiceSchema.cs | идемпотентная гарантия служебных таблиц на сессию (кэш по пути БД) |
| `FieldRegistry` | FieldRegistry.cs | реестр колонок IO_LIST: типы, алиасы, обязательность |
| `IOListRepository` | IOListRepository.cs | CRUD IO_LIST, upsert по Tag+System+Plant, выборки |
| `ImportService` | ImportService.cs | конвейер импорта Excel → IO_LIST (нормализация, аудит, транзакция) |
| `ExcelReader` / `ExcelExportService` | *.cs | чтение/запись `.xlsx` (ClosedXML) |
| `Validator` | Validator.cs | E001–E009 / W001–W008 |
| `AuditService` / `EventLogger` | *.cs | `IO_LIST_Audit` и журнал событий (лимит 5000) |
| `MappingService` | MappingService.cs | шаблоны маппинга (`MappingTemplates`) |
| `ModbusMapService` | ModbusMapService.cs | импорт/хранение `MODBUS_MAP` («Сравнение»/«Заменить») |
| `ModbusLinkService` | ModbusLinkService.cs | связывание IO_LIST ↔ MODBUS_MAP (эвристики ролей) |
| `ModbusLinkRuleService` | ModbusLinkRuleService.cs | ручные маски `MODBUS_LINK_RULES` |
| `MaskPattern` | MaskPattern.cs | компиляция масок `{Кол}`, `*`, `?`, `(±N)`, Score/FindBest |
| `ScadaTechObjectService` | ScadaTechObjectService.cs | F12: генерация `.xlsx` «Тех.объекты» + `SCADA_TYPE_MAP` |
| **`ModbusBlockPlannerService`** | ModbusBlockPlannerService.cs | F13: **план блоков** (группировка, пакеты, резерв), настройки `SCADA_MB_SETTINGS`, правила SlaveId/Typno |
| **`ScadaTemplateXmlService`** | ScadaTemplateXmlService.cs | F13: **генерация XML** мастер-блоков из шаблона, маппинг объектов, токены |
| **`ScadaDataOutXmlService`** | ScadaDataOutXmlService.cs | F13: **data-цепочки** (Data_Out/Data_In), биты, F75/F12/F97/F105 |
| `ScadaAlgoService` / `ScadaAlgoXmlService` | *.cs | F65: типовые алгоритмы (TYPNO, привязки, пагинация файлов) |
| `DbProbe` | DbProbe.cs | пробное соединение с лимитом 5 с, распознавание блокировок |
| `TagParser` / `ValueConverter` | *.cs | парсинг тегов, конвертация шкал |
| `ReportBuilder` / `TableCreator` | — | отчёты истории, резервные копии |

### 4.4 Типовой поток любой SCADA-задачи

```
Пользователь → Окно (Views/*.xaml) → VM (ViewModels/*)
   → LoadSettings() / LoadXxx()  (читает Name/Value-таблицу настроек)
   → [кнопка] Build/Generate    (VM собирает входные данные из БД)
   → Service.BuildPlan(...)     (ModbusBlockPlannerService — план в памяти)
   → Service.GenerateXml(...)   (ScadaTemplateXmlService / ScadaDataOutXmlService — XML в файл)
   → предупреждения/ошибки → панель окна + EventLogger
   → SaveSettings()             (обратная запись Name/Value)
```

## 5. Модель данных и база Access

### 5.1 IOPoint / IO_LIST

- Дерево `IOPoint` (Models/IoPoint.cs) описано **полностью** в `IOclass.md` (разделы «Полная иерархическая структура», «Детальное описание всех классов»): группы `Identification` (Tag, System, Plant, Loop, Logic, IoType, Location, Controller…), `Signal` (Service, SignalType, ExType…), `Ranges` (Primary/Secondary/Alarms — TEXT), `Modbus` (M_-поля: `M_PlcTag`, `M_SlaveIdMain`, `M_Address`, `M_Bit`, `M_DataType`, `M_FuncCode`, `M_Timeout`, `M_BusBlockRes`, `M_BusDataOutRes`, `M_Bit_Offset`…), `Cable`, `Equipment`, `Revision`, `Typno`.
- Таблица `IO_LIST` — 130 колонок; DDL и правила записи — в `IOclass.md` («DDL», «Правила записи (при импорте Excel)»). Реестр колонок в коде — `Data/FieldRegistry.cs`.
- **Идентичность тега:** `Tag + System + Plant` (Trim, регистр не важен). Уникальный индекс по `Tag` в таблице.
- `M_*`-поля — результат связывания с картой Modbus; **все SCADA-генераторы читают только их** (`M_PlcTag` обязателен для попадания в план).

### 5.2 Служебные таблицы (имена — константы `Data/Schema.cs`)

| Таблица | Назначение |
|---|---|
| `IO_LIST_Audit` | история изменений (append-only) |
| `IO_LIST_DublicateTag` | копии повторяющихся тегов |
| `MappingTemplates` | шаблоны маппинга Excel→колонки |
| `MODBUS_MAP` | карта адресации (Function 02/03/06 + Interface parameters) |
| `MODBUS_LINK_RULES` | ручные правила/маски связывания |
| `SCADA_TYPE_MAP` | соответствие типов IO_LIST → SCADA |
| `SCADA_TEHOBJ_SETTINGS` | настройки генерации «Тех.объекты» |
| `SCADA_LINK_SETTINGS` | настройки связывания |
| `SCADA_MB_SETTINGS` | **настройки генерации блоков (Name/Value)** |
| `SCADA_MB_SLAVE_RULES` | правила «станция → SlaveId» |
| `SCADA_MB_TEMPLATE` | XML-шаблоны в БД (режим «Код») |
| `SCADA_MASTER_TEMPLATES` / `SCADA_DATAOUT_TEMPLATES` / `SCADA_DATAIN_TEMPLATES` | шаблоны data-цепочек |
| `SCADA_TEMPLATE_VARS` | **маппинги шаблонов (скоупы dataout/datain/masterobj)** |
| `SCADA_TYPNO_MAP` | правило Typno → шаблон привязок |
| `SCADA_ALGO_SETTINGS` / `SCADA_ALGO_TEMPLATES` / `SCADA_ALGO_BINDINGS` / `TYPNO` | типовые алгоритмы |

- **Автамиграции:** `TableCreator.EnsureServiceTables` / `EnsureColumns` создают таблицы и недостающие колонки идемпотентно (ALTER TABLE ADD); повтор при сбое. Новую таблицу/колонку добавлять ТОЛЬКО через существующие механизмы (DDL-константа в `Schema.cs` + вызов в `EnsureServiceTables`), самодельных `CREATE TABLE` в бизнес-коде избегать.
- **Персист настроек (общий паттерн):** таблица `Name/Value` (одна строка = одна настройка). Ключи перечислены в `ModbusBlockPlannerService.SettingKeys` (см. §6.3), `LoadSettings()` читает с ретраями (3 попытки при блокировке), `SaveSettings()` пишет и удаляет устаревшие (`ObsoleteSettingKeys`). При добавлении настройки: **свойство в класс настроек (INPC + Clone) → ключ в SettingKeys → Load → Save → UI → тест roundtrip**.

## 6. SCADA-генератор блоков Modbus (главный конвейер)

Это самая сложная и востребованная область проекта. Все недавние фичи (F85–F105) — здесь.

### 6.1 Что генерируется

Файл XML с корнем **`<BufScadaPOUS>`** — блоки опроса Modbus TCP/RTU + data-цепочки для импорта в SCADA. Полная расшифровка формата — `SCADA_XML_STRUCTURE.md` §3. Минимальный скелет:

```xml
<BufScadaPOUS>
  <Common .../>
  <OnePOU NAME="..." isFBD="1">
    <ISAGraf>
      <Blocks>
        <Block GROBJTYPE="31" Info="_TAG" T11ID="1000001">
          <Graphics X="..." Y="..." WIDTH="..." HEIGHT="..."/>
          <Params T11Text="_TAG" CI="1" CO="1" VMODE="0" IsaObjId="885" cardId="2000001">
            <IV>6(FALSE),TRUE,...</IV>
          </Params>
        </Block>
      </Blocks>
      <Links>
        <Link ...>
          <PointList PL="(x,y);(x,y);"/>
          <FirstPoint FP="T11ID|True|Result|0,0,0,0"/>
          <LastPoint  LP="T11ID|True|0|0,0,0,0"/>
        </Link>
      </Links>
    </ISAGraf>
  </OnePOU>
</BufScadaPOUS>
```

Ключевые инварианты:
- **`T11ID` и `cardId` должны быть уникальны в пределах файла** (split-экспорт не переиспользует ID; базовые значения — настройки `BaseT11Id`/`BaseCardId`).
- Порядок детей `<Params>`: `T11Text, CI, CO, VMODE, IsaObjId, cardId` → `<IV>`.
- `FP`/`LP` ссылки обязаны указывать на существующие `<Block T11ID>`; при удалении блока **удалять все связи с ним и примитивы `[TEXT]`, ссылающиеся на него** (иначе «висячие» ссылки = битый импорт в SCADA).
- Имя блока в `Info`/`T11Text` = итоговое имя объекта (марка тега, константа или имя мастера).

### 6.2 Конвейер (окно F13 `ScadaModbusBlocksWindow`)

1. **Источник:** точки текущей таблицы с заполненным `M_PlcTag` (режим «Адрес»/«Адрес/бит», режимы чтения; функции 06/15/16 пропускаются).
2. **План** (`ModbusBlockPlannerService`): группировка по (IP, порт, функция) → сортировка по адресу → пакеты по политике (`Contiguous`/`Cover`/`FixedSize`) с лимитом `min(MaxCount, 123 регистров / 246 бит)`; резервный twin-пакет (`M_SlaveIdBackup`, колонки `M_BusBlockRes`/`M_BusDataOutRes`).
3. **Мастер-блоки** (`ScadaTemplateXmlService`): XML-шаблон (файл или БД) → копия на пакет → подмена токенов `{main}/{reserve}`, имя мастера пакета, **перечень «Объекты мастер-шаблона»** (скоуп `masterobj`: имя = автоимя мастера + окончание `NameSuffix`, значение = колонка IO_LIST или литерал → `<IV>`).
4. **Data-цепочки** (`ScadaDataOutXmlService`): data-шаблон (Data_Out/Data_In) копируется по числу сигналов пакета; канал имени (колонка/шаблон → `Info`), канал значения (литерал → `<IV>`), **биты** (M_Bit → марка с номером бита; режим «Адрес/бит»; default из Literal/Typno; запрет зашитого FALSE — F97), **F105** (удаление непривязанных битовых объектов при включённой настройке).
5. **Экспорт** (`ScadaTemplateXmlService.Export/ExportSplit`): split-по файлам, анти-наложение кластеров, зазоры (`DataChainGap`, `DataPacketGap`), комментарии POU (`PouCommentPattern`), превью в окне.

### 6.3 Настройки F13 (`ScadaMbGenSettings`, таблица `SCADA_MB_SETTINGS`)

Полный список ключей (`ModbusBlockPlannerService.SettingKeys`, L687–701):
`Policy, MaxCountRegisters, MaxCountBits, MaxGap, DefaultPort, DefaultSlaveId, DefaultTimeoutMs, TemplateMode, TemplateFilePath, MasterTcpTemplateName, MasterRtuTemplateName, DataOutTemplateMode, DataOutTemplateFilePath, BlockNamePattern, BlockNamePatternRtu, FormatAddress, DataOutTemplateName, BlockNamePatternReserve, BlockTokenReserve, PouNamePattern, DataOutPouNamePattern, BaseT11Id, BaseCardId, ColumnsPerRow, RowStep, ColumnStep, DataChainGap, PouCommentPattern, DataOutPouCommentPattern, DataPacketGap, DataChainStep, RemoveUnboundBitObjects`.

Правила:
- Класс настроек — `ScadaMbGenSettings` (`Models/ModbusBlockPlan.cs`, L764+): свойства с INPC, **обязательно обновлять `Clone()`** при добавлении свойства.
- Default нового флага — **false** (без побочных эффектов для существующих пользователей).
- Устаревшие ключи — в `ObsoleteSettingKeys` (удаляются при Save).

### 6.4 Маппинги шаблонов (`SCADA_TEMPLATE_VARS`)

Таблица: `Template`, `Scope` (`dataout` | `datain` | `masterobj`), `Key`, `IoColumn`, `Literal`, `NameSuffix`, `BitMode`, `BitNo` (состав колонок — DDL в `IOclass.md`, «DDL служебных таблиц генерации блоков»).

- **Скоупы `dataout`/`datain`:** ключ = имя объекта шаблона; канал имени (`IoColumn` → колонка IO_LIST, либо строковый шаблон `{Кол}` → `Info`/`T11Text`), канал значения (`Literal` → `<IV>`). Неразрешённая колонка/пустой маппинг = предупреждение, не тихая подстановка.
- **Скоуп `masterobj` (F101–F104):** ключ = имя мастер-объекта шаблона; `NameSuffix` — окончание имени (`_MB_X_MAIN` + `.Busy`; пустое — окончание из самого шаблона), `IoColumn`/`Literal` — значение. В гриде окна — «Объекты мастер-шаблона», команды Fill/Save (`ScadaModbusBlocksViewModel.FillMasterObjectVars/SaveMasterObjectVars`).
- Общие точки подмены: `ApplyMasterObjMapping101` (ScadaTemplateXmlService), `SubstituteWithMasterMapping`, `ApplyMasterObjectSuffix`, `ApplyMasterObjMapping101` учитывает разбор префикса до токена мастера (баг F102/F103).
- ВАЖНО: **канал имён data-шаблонов НЕ переименовывает объекты с токенами мастера** — это отдельная логика мастер-объектов.

### 6.5 Биты и марки (F75/F12/F96/F97/F105)

- **F75 `TagNameFor`:** превращает марку тега в имя SCADA — **добавляет ведущий `_`**, если марка содержит `_` (`TAG_ONE` → `_TAG_ONE`). Это проверено смоук-тестом 11 — учитывать при ожиданиях.
- Битовый сигнал: колонка `M_Bit` (номер бита 0..15), режим «Адрес/бит», настройка «Смещение битов» (`M_Bit_Offset`). Объекты `Bit0..Bit15` шаблона заменяются на марку сигнала с этим битом.
- **F97:** запрет зашитого литерала `FALSE` в `Info` — по умолчанию пишется имя объекта.
- **F105:** настройка `RemoveUnboundBitObjects` (default false): битовый объект, у которого марка не привязалась (нет точки с нужным `M_Bit` либо пустая `M_Marka`), удаляется из data-цепочки **вместе со связями, подписями `[TEXT]` и карточкой**; предупреждение F105. Сохраняется, если есть явная строка маппинга (`IoColumn`/`Literal` непустые) либо режим «Адрес». Реализация: `ScadaDataOutXmlService.ApplyBindingSubstitutions` возвращает `bool`; вызывающий код при `false` не добавляет клон в POU и ведёт `removedT11` для обрезки связей/примитивов.

### 6.6 Именование и санитизация

- Все генерируемые имена проходят санитизацию: спецсимволы → `_`, двойные `_` → один, предупреждения в панель.
- Паттерны имён блоков/POU (`BlockNamePattern`, `PouNamePattern`…) поддерживают токены `{ИмяКолонки}`; ведущий `_` паттерна не пропускается.
- POU создаётся по шаблону (ID/POUNum из шаблона, холст по bbox).

## 7. Прочие генераторы (кратко)

- **F12 «Тех.объекты»** (`ScadaTechObjectService`, окно `ScadaTechObjectsWindow`): `.xlsx` лист `TEHOBJ`, шапка 4 строки, 20 колонок; Марка из поля (по умолчанию `Tag`) с `_` вместо `-`; Тип объекта через `SCADA_TYPE_MAP`; источник — точки с `M_PlcTag`. Спецификация колонок — файл «Генерация технологических объектов Modbus.md».
- **F65 типовые алгоритмы** (`ScadaAlgoService`/`ScadaAlgoXmlService`, окно `ScadaAlgoInstancesWindow`): экземпляр = контур `Loop`; марки через привязки «сигнал шаблона → строка TYPNO» + маски `MarkFor`; пагинация файлов (`InstancesPerFile`, `IdStride`); гейты: валидность шаблона, наличие TYPNO, корректность привязок (без частичной генерации).
- **Импорт/связывание** (§3–§5 ТЗ.md): окна `ImportWindow` (профили IO_LIST/MODBUS_MAP), `ModbusAddressingWindow` (обновление связей/адресации, редактор масок). Связывание: `ModbusLinkService` + `MaskPattern`, состояния LinkState («привязано», «привязано частично», «привязано не по полным совпадением!», «не привязано»).

## 8. UI: устройство окон и добавление элементов

- Окна WPF в `Views\` (XAML + code-behind), VM в `ViewModels\`. DataContext создаётся в code-behind окна; наружу VM отдаётся через свойство.
- Окно F13 (`ScadaModbusBlocksWindow.xaml`) — крупнейшее: `GridSplitter`-разделение секций (настройки / шаблоны / превью), вкладки (① мастер-объекты, data-шаблоны, Typno, SlaveId-правила), гриды `DataGrid` с `ItemsSource`-привязками к `ObservableCollection`, панель ошибок (красная) + журнал, именованные профили настроек, превью пакетов master-detail.
- Сетка настроек F13: строка = `RowDefinition`, лейбл + элемент (TextBox/CheckBox/ComboBox) в двух колонках; **добавление элемента = +1 `RowDefinition`** (так делалось для F105, чекбокс в строке 19).
- Чекбокс настройки: `<CheckBox IsChecked="{Binding Settings.RemoveUnboundBitObjects, Mode=TwoWay}" .../>` — свойство в `ScadaMbGenSettings` обязано быть INPC.
- Стили — `Resources/Styles.xaml`; новых ресурсов без нужды не заводить.
- Тултипы — по-русски, раскрывающие поведение (как у F105).

## 9. История F-вех (шпаргалка по зонам)

F-нумерация — исторические метки задач (с версии 1.0 нумерация версии отдельная, `AppInfo.Version`; F-метки остаются в коде/IOclass.md). Полные описания — в `IOclass.md` (разделы F34…F105) и `ТЗ.md`.

| Метка | Суть | Ключевые файлы |
|---|---|---|
| F1 | ядро: открытие БД, таблицы, CRUD | AppServices, TableCreator |
| F2 | импорт: замена документов, дубликаты, W-коды | ImportService |
| F7 | аудит INSERT/UPDATE | AuditService |
| F8 | карта Modbus: импорт Register Table | ModbusMapService |
| F9 / F9.11 | связывание + маски + UpdateAddressing | ModbusLinkService, MaskPattern |
| F12 | генерация «Тех.объекты» | ScadaTechObjectService |
| F13–F16 | генерация блоков: план, шаблон, SlaveId, Typno | ModbusBlockPlannerService, ScadaTemplateXmlService |
| F20 | политики пакетов/лимиты | ModbusBlockPlannerService |
| F34 | автозаполнение входов мастера, фикс маппинга приёмника | ScadaTemplateXmlService |
| F37 | ObjectTag из PlcTag | ModbusMapService |
| F61–F64 | комментарии POU, зазоры, шаг цочки | ScadaMbGenSettings |
| F65 | типовые алгоритмы | ScadaAlgoService/ScadaAlgoXmlService |
| F68/F69 | reserve-имя и reserve-токен мастера | ScadaTemplateXmlService |
| F75 | TagNameFor: ведущий `_` у марки с `_` | ScadaDataOutXmlService |
| F80 | карточки data-объектов (F97/F80 ветка) | ScadaTemplateXmlService |
| F85–F95 | превью/шаблоны/обратная запись IP/маски/удаление мёртвого кода | VM F13 |
| F96/F97/F98 | битовый маппинг, запрет FALSE, превью data-шаблона | ScadaDataOutXmlService |
| F100 | новое окно генерации (F100-серия) | Views/VM |
| **F101–F104** | перечень «Объекты мастер-шаблона» (`masterobj`, `NameSuffix`), баги имён `_MAIN.BUSY.Busy` | ScadaTemplateXmlService, VM, Test8/10 |
| **F105** | удаление непривязанных битовых объектов (настройка default off) | ScadaDataOutXmlService, ModbusBlockPlan, XAML, Test11 |

## 10. Смоук-тесты: устройство и добавление нового

### 10.1 Устройство

- Проект `IO_PJT.Smoke` — консольный, ссылка на `IO_PJT`; **partial-класс `IO_PJT.Smoke.Program`** разбит на файлы по тестам (см. §3).
- `Program.cs`: `Check(cond, name)` (счётчики OK/FAIL), `Log`, `RunTest(title, body)` (исключение = FAIL теста, остальные работают), `Main` регистрирует тесты и печатает `ИТОГО: OK N, FAIL M`; код выхода 0/1.
- Помощники: `NewDbPath()` (%TEMP% `iosmoke_<guid>.accdb`), `DeleteDb` (GC.Collect перед удалением — иначе lock ACE), `CreateDb(path)` (служебные + IO_LIST + MODBUS_MAP), `MakePoint(tag, ioType, service)`.
- Внешние файлы НЕ нужны: xlsx генерируется через ClosedXML в коде (`Gen.Common.cs`), шаблоны — встроенные строки или ресурсы теста.

### 10.2 Как добавить тест (пошагово)

1. Создать файл `IO_PJT.Smoke\TestNN.<Тема>.cs` с `public static partial class Program` и методом `private static void TestNN_Name()`.
2. Внутри — `Check(...)` на каждую проверку (имя — по-русски, суть проверки в имени).
3. Зарегистрировать строку `RunTest("NN. <Название>", TestNN_Name);` в `Main` (Program.cs).
4. Собрать решение, запустить DLL напрямую (§2), убедиться: `ИТОГО: OK <старое+new>, FAIL 0`.
5. Если функция регрессионная — добавить сценарий «до/после» (вкл/выкл) как в Test11.

### 10.3 Текущий набор (11 тестов, 120 проверок)

1. Схема БД и реестр колонок · 2. IO_LIST upsert+аудит · 3. Импорт MODBUS_MAP · 4. Маски · 5. Связывание и адресация · 6. План блоков · 7. Генерация XML · 8. Объекты мастер-шаблона · 9. Реальный шаблон `ModbusTCP_reserv_v0.xml` · 10/10.1. Баг `.Busy` · 11. F105.

## 11. Карта документации (что где читать)

| Файл | Что содержит | Когда читать |
|---|---|---|
| `IO_PJT\AI_SPEC.md` (этот) | архитектура, правила, ловушки, чек-листы | перед первой задачей; при сомнениях в процессах |
| `Work\ТЗ.md` (корень репо: `Work\Work\ТЗ.md`) | **утверждённое ТЗ рев.2**: поведение всего функционала, перечень изменений, приёмка | когда нужно точное поведение (что именно должен делать функционал) |
| `IO_PJT\IOclass.md` | дерево IOPoint, DDL всех таблиц, правила записи Excel, **журнал фич F34–F105** | при работе с моделью/колонками/DDL; при поиске «что уже делали в Fxx» |
| `IO_PJT\SCADA_XML_STRUCTURE.md` | полная расшифровка XML SCADA (BufScada, BufScadaPOUS, TEHOBJ), примеры | при любой работе с генераторами XML |
| `IO_PJT\Генерация технологических объектов Modbus.md` | колонки листа TEHOBJ, маппинг типов | задачи по F12 |
| `IO_PJT\Modbus RTU_TCP block.md` | заметки по блокам опроса | задачи по плану пакетов |
| `IO_PJT\IOLISTtoDB_v0.MD` | архив v0 (не использовать как источник истины) | только исторически |
| `IO_PJT\_Specification\*` | исходные задания заказчика, скриншоты, примеры xlsx | уточнение требований |
| XML-комментарии в коде | пояснения F-метками (`/// <summary>F105: …`) | при чтении конкретных методов |

Правило: **источник истины по поведению — ТЗ.md, по структуре данных — IOclass.md, по формату файлов — SCADA_XML_STRUCTURE.md.** Расхождения кода с ТЗ = баг (или ТЗ нужно ревизовать явно, добавив строку в «Перечень изменений»).

## 12. Ловушки среды и инструментов (чтобы не потратить часы)

1. **PowerShell и кириллица:** длинные команды (примерно >400–600 символов) с кириллицей «рвут» UTF-8 — теряются первые букв слов, кириллические имена файлов в путях ломаются (`Г` → пусто). Правила: команды держать короткими; пути с кириллицей подавать через `read_files`/`editor`, не через PowerShell; либо кодировать символы (`[char]0x432` и т.п.).
2. **Режим Plan** (без права правок) блокирует `Remove-Item`, `>`-редиректы и прочие state-changing команды. Рабочие паттерны: логи в `%TEMP%` через `Start-Process -RedirectStandardOutput` либо инструменты чтения/редактирования файлов.
3. **Лимит инструментов:** `editor` принимает ≤6000 символов на операцию — большие вставки разбивать на части (заменять хвостовую строку предыдущей части на «хвост + новый кусок»). `read_files` — до 2000 строк/47к символов за вызов, длинные файлы листать `start_line/end_line`.
4. **`search_codebase` не индексирует проект смоука** (`IO_PJT.Smoke`) — искать в нём через `Select-String`/`findstr`. По основному проекту — search_codebase удобен.
5. **Кэш и «старые копии»:** смоук запускать только от собранной DLL (§2); после правок — обязательно пересобрать решение, иначе тесты бегут на старом коде.
6. **`dotnet build` solutions:** `.sln` = `IO_PJT\IO_PJT.sln`. Сборка смоука включена в решение (Debug по умолчанию).
7. **Файлы данных в репозитории:** не удалять/не «чистить» `Work\Work\*` (xlsx/accdb/шаблоны) — это данные пользователя; в коммит их включать только по явной просьбе. Временные логи `_f99_*.log` — в `.gitignore`.
8. **Кодировка:** исходники UTF-8 (часто с BOM); при записи файлов сохранять кириллицу; НЕ пересохранять файлы в ANSI. XAML/строки с кириллицей не «экранировать».
9. **OleDb:** рабочий поток — один `using var cn/using var cmd`; повторы при блокировке (`DbProbe.IsLockError`, 3 попытки, `Thread.Sleep(300*attempt)`); `CommandTimeout = DbProbe.TimeoutSeconds`; параметры через `OleDbArgs` (защита от инъекций), LIKE-спецсимволы экранировать.
10. **Удаление объектов в XML-генераторах:** всегда проверять связность (Links FP/LP, примитивы, карточки) — половина исторических багов F-серии про «висячие» ссылки после удаления/переименования.
11. **git:** ветка `main`, origin `github.com/ahtoh80-hub/Work`. Коммиты — с осмысленным русским сообщением; push только после зелёного смоука и сборки Debug+Release.

## 13. Чек-лист новой фичи (проходить ПОСЛЕДОВАТЕЛЬНО)

0. **Понять требование.** Найти в `ТЗ.md`/`IOclass.md` описанные смежные механизмы; определить, какая F-метка будет у задачи (продолжить нумерацию: следующая после F105 — F106). Сформулировать: входные данные, поведение, что НЕ должно сломаться (регрессии).
1. **Прочитать целевые файлы целиком** (или значимые фрагменты) + их тесты в смоуке.
2. **Модель/настройки** (если нужна новая настройка): свойство INPC в класс настроек + `Clone()` → ключ в `SettingKeys` → ветка в `LoadSettings` → ветка в `SaveSettings` → default безопасный (false/прежнее значение).
3. **Бизнес-логика** в сервисе (sealed-класс, `using var` для OleDb, ретраи при блокировке, предупреждения — отдельным списком с дедупликацией, не `MessageBox`).
4. **UI:** XAML (+1 RowDefinition при добавлении в сетку F13), привязка TwoWay, тултип по-русски; команды/свойства в VM через `Set(ref…)`/`RelayCommand`.
5. **Обратные пути:** сохранение настроек, превью, «Проверить шаблон» — обновить, если логика их затрагивает.
6. **Смоук:** новый `TestNN` (или расширение существующего) с регрессионными ветками (default вкл/выкл), регистрация в `Main`.
7. **Документация (обязательно):**
   - `IOclass.md` — новый раздел `### Fxxx — <суть> (дата)` в конце журнала (стиль предыдущих записей: что, где, как, итог смоука);
   - `Work\Work\ТЗ.md` — строка в «Перечень изменений» (ревизия +1) + правка описания в соответствующем §;
   - `AppInfo.cs` — `Version` при пользовательской заметной функции (X.Y);
   - XML-комментарии `/// <summary>Fxxx: …` у изменённых методов.
8. **Сборка:** Debug и Release — **0 ошибок** (предупреждения, появившиеся от ваших правок, убрать; старые CS8600 не трогать без нужды).
9. **Прогон смоука:** `ИТОГО: OK <выросло>, FAIL 0`.
10. **Коммит и push:** `git add -- <только изменённые файлы>` (не захватить данные пользователя), сообщение по-русски вида `Fxxx: суть`, затем `git push origin main`. Отчитаться: что сделано, версия, счётчик смоука.

## 14. Приёмка (Definition of Done)

- [ ] Сборки Debug + Release — 0 ошибок.
- [ ] Смоук — 0 FAIL, счётчик вырос (новые проверки добавлены).
- [ ] Default-поведение не изменилось (новые флаги = false / эквивалент старого пути).
- [ ] Нет «висячих» ссылок в XML (если менялась генерация): T11ID/cardId уникальны, FP/LP ссылаются на существующие блоки.
- [ ] Настройки: ключ + Load + Save + Clone + roundtrip-тест.
- [ ] Документация обновлена (IOclass.md, ТЗ.md ревизия, AppInfo при необходимости).
- [ ] Кириллица в новых строках не повреждена (проверить глазами/чтением файла).
- [ ] В коммит не попали посторонние файлы (данные, логи, bin/obj не вручную).
- [ ] Push выполнен; рабочее дерево чистое (`git status` без неожиданностей).

## 15. Рецепты типовых изменений (быстрые ответы «как»)

### 15.1 Добавить настройку генерации (галочка/число)

1. `Models/ModbusBlockPlan.cs` → `ScadaMbGenSettings`: свойство `{ get; set; }` через `Set(ref…)` + в `Clone()`.
2. `Services/ModbusBlockPlannerService.cs`: имя ключа в `SettingKeys` (~L687), чтение в `LoadSettings()` (после чтения словаря `values`, парсинг bool/числа с fallback на default), запись в `SaveSettings()`.
3. `Views/ScadaModbusBlocksWindow.xaml`: элемент в сетку (+1 `RowDefinition`), `Mode=TwoWay`, тултип.
4. Потребитель логики: ветка в генераторе (обычно `ScadaDataOutXmlService`/`ScadaTemplateXmlService`), **default = прежнее поведение**.
5. Тест: ветки выкл/вкл + roundtrip Save/Load в смоуке.

### 15.2 Добавить колонку в служебную таблицу

1. `Data/Schema.cs`: колонка в DDL-константу таблицы.
2. `Services/TableCreator.cs` → `EnsureServiceTables`/`EnsureColumns`: `ALTER TABLE … ADD COLUMN` идемпотентно (проверка существования через схему OleDb).
3. Чтение/запись в сервисе таблицы; при добавлении в `SCADA_TEMPLATE_VARS` — не забыть грид VM (`LoadDataTemplateVars`/`SaveDataTemplateVars` или `…MasterObjectVars`).
4. Тест: создание свежей БД + повторный прогон (идемпотентность).

### 15.3 Добавить предупреждение генерации (код Fxxx)

- Формировать строку вида `Fxxx: <суть> (<объект>)`, добавлять в общий список предупреждений с **дедупликацией** (HashSet по «пакет+объект»), отдавать в UI-панель; критичные ошибки — отдельно (`Errors`), не смешивать.
- Примеры: «F105: удалён непривязанный битовый объект …».

### 15.4 Добавить смоук-тест

См. §10.2. Не создавать внешних файлов; данные — через ClosedXML/строки; БД — `%TEMP%`; после теста `DeleteDb`.

### 15.5 Изменить имя/машинную подстановку в XML

- Токены мастера: `SubstituteMasterTokens`/`ContainsMasterToken` (ScadaTemplateXmlService) — тест 10.1.
- Имя объекта data-цепочки: `TagNameFor` (F75, ведущий `_`), маппинги `SCADA_TEMPLATE_VARS`, `ApplyMasterObjectSuffix`/`SubstituteWithMasterMapping` (F101–F104).
- После правок — обязательно прогнать тесты 8, 9, 10, 10.1, 11.

### 15.6 Новое окно/меню

1. `Views/NewWindow.xaml(+.cs)`, VM в `ViewModels` (наследник `ViewModelBase`).
2. Пункт в `Views/MainWindow.xaml` (меню SCADA/Импорт) + обработчик в code-behind (открыть окно, передать `DbPath`/`AppServices`).
3. Сервис окна — свой экземпляр от текущего `DbPath` (как у F13).
4. Справка (`HelpWindow`) и «О программе» — при необходимости.

### 15.7 Регресс «раньше работало»

- Перед изменением найти в смоуке существующие проверки затрагиваемого поведения; сохранить их в прежнем виде.
- Добавить новую ветку **дополнительно**, не переписывая старую (пример: Test11 проверяет и default-ветку, и включённую настройку).

## 16. Case study: как решалась задача F105 (образец рассуждения)

Исходное требование заказчика: *«Настраиваемое удаление битовых объектов, которые остались без привязки. Битовые объекты, у которых марка не привязалась, по настройке должны удаляться из цепочки. По умолчанию — не удалять.»*

Как это разбирается (повторять такой порядок мышления):

1. **Найти место в коде.** «Битовые объекты» → поиск `Bit\d`/`M_Bit` → `ScadaDataOutXmlService.ApplyBindingSubstitutions` (там же F75/F96/F97). «Цепочка» → копирование data-шаблона в POU + Links + примитивы `[TEXT]` + карточка.
2. **Сформулировать условие удаления** (все конъюнкции): настройка вкл ∧ имя битовое ∧ битовая ветка активна (режим «Адрес» не трогаем) ∧ марка не нашлась (нет точки с `M_Bit` либо пустая `M_Marka`) ∧ нет явной строки маппинга (иначе пользователь явно что-то хотел).
3. **Выбирать механизм настройки:** настройка генерации → `ScadaMbGenSettings` + `SettingKeys` + Load/Save/Clone + чекбокс в сетке F13. Default = false = старое поведение (регрессия исключена по построению).
4. **Сохранить связность XML:** при удалении блока — `removedT11` → обрезка Links (FP/LP) и примитивов; карточку не создавать; предупреждение `F105: …` с дедупликацией.
5. **Расширить `bool`-контракт:** `ApplyBindingSubstitutions` → `bool` («клон добавлен»), call-site ветвится. Изменение сигнатуры — безопасно, потому что метод внутренний.
6. **Тест:** Test11 со всеми ветками (default / вкл / вкл+строка / вкл+режим «Адрес» / roundtrip настроек) — и регресс всех прежних тестов не тронут.
7. **Документы:** запись в конце `IOclass.md`, строка ревизии в `ТЗ.md`, `AppInfo.Version` 1.0 → 1.1, `/// F105` в коде.
8. **Приёмка:** Debug+Release 0 ошибок, смоук 120/0, коммит `F105: …`, push.

Такой же порядок — для любой новой задачи (§13).

## 17. Список окон и точек входа (ориентир по меню)

| Меню/кнопка | Окно | VM |
|---|---|---|
| Файл → Открыть базу… | встроенный диалог | MainViewModel |
| Импорт → Импорт IO LIST из Excel… / Modbus_MAP… | `ImportWindow` (одно окно, профиль) | `ImportViewModel` |
| Импорт → Обновление адресацию Modbus… | `ModbusAddressingWindow` | `ModbusAddressingViewModel` |
| Импорт → История изменений… | `HistoryWindow` | `HistoryViewModel` |
| SCADA → Генерация перечня Modbus (Тех.объекты)… | `ScadaTechObjectsWindow` | `ScadaTechObjectsViewModel` |
| SCADA → Генерация блоков Modbus (XML)… | **`ScadaModbusBlocksWindow` (F13)** | **`ScadaModbusBlocksViewModel`** |
| SCADA → Генерация экземпляров типовых алгоритмов… | `ScadaAlgoInstancesWindow` | `ScadaAlgoInstancesViewModel` |
| Таблица → создать/копия/очистить/обновить | встроенные диалоги | MainViewModel |
| Справка → Руководство / О программе | `HelpWindow` / диалог (`AppInfo.Version`) | — |

Карточка сигнала — `CardWindow`; редакторы XML-шаблонов — `TemplateCodeWindow` (единый флоу «файл → окно кода → сохранить в БД»).









