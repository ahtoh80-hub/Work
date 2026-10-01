# Техническое задание на разработку класса IOPoint
## Полная структура и компоненты для формирования кода

---

## 1. ОБЩИЕ ТРЕБОВАНИЯ

### 1.1. Назначение
Разработать класс `IOPoint` для представления точки ввода-вывода в системах промышленной автоматизации. Класс должен обеспечивать полный цикл работы с данными: хранение, валидацию, импорт/экспорт, работу с репозиторием и сопоставление полей.

### 1.2. Технологический стек
- **Язык:** C# (.NET 6.0 или выше)
- **Сериализация:** JSON, XML, бинарная
- **Работа с Excel:** EPPlus или ClosedXML
- **Работа с Access:** System.Data.OleDb
- **Хранение:** Репозиторий с поддержкой кэширования

---

## 2. ОСНОВНЫЕ КЛАССЫ И ИХ СВОЙСТВА

### 2.1. Класс IOPoint

**Назначение:** Главный контейнер, объединяющий все аспекты точки ввода-вывода

| Свойство | Тип | Описание | Обязательность |
|:---|:---|:---|:---|
| `Identification` | `Identification?` | Идентификационные данные точки | Нет |
| `Signal` | `Signal?` | Технологические параметры сигнала | Нет |
| `Ranges` | `Ranges?` | Диапазоны измерений и уставки | Нет |
| `Cable` | `Cable?` | Данные о кабельном хозяйстве | Нет |
| `Equipment` | `Equipment?` | Привязка к оборудованию контроллера | Нет |
| `Revision` | `Revision?` | Информация о версиях и ревизиях | Нет |
| `Status` | `IOPointStatus` | Текущий статус точки | Да |
| `CurrentValue` | `decimal?` | Текущее значение сигнала | Нет |
| `LastUpdate` | `DateTime?` | Время последнего обновления | Нет |
| `IsValid` | `bool` | Флаг валидности всех данных | Да |
| `IsTagUnique` | `bool` | Флаг уникальности Tag | Да |
| `DataQualityScore` | `int` | Оценка качества данных (0-100) | Да |
| `Id` | `Guid` | Уникальный идентификатор | Да |
| `CreatedAt` | `DateTime` | Дата создания | Да |
| `ModifiedAt` | `DateTime` | Дата последнего изменения | Да |
| `IsDeleted` | `bool` | Флаг удаления | Да |
| `Version` | `int` | Версия записи | Да |

### 2.2. Класс Identification

**Назначение:** Идентификационные данные точки

| Свойство | Тип | Описание | Ограничения |
|:---|:---|:---|:---|
| `Code` | `int?` | Порядковый номер строки | - |
| `Area` | `int?` | Номер технологической установки | 1-6 цифр |
| `Title` | `string?` | Номер установки/зоны | - |
| `MainLoop` | `string?` | Основной технологический контур | - |
| `SubLoop` | `string?` | Вложенный субконтур | - |
| `ProcessLoop` | `string?` | Технологический контур | - |
| `Tag` | `string?` | Уникальный тег устройства | **ДОЛЖЕН БЫТЬ УНИКАЛЬНЫМ!** |
| `ParsedTag` | `InstrumentTag?` | Распарсенный тег | - |
| `ProcessTag` | `string?` | Технологический тег | - |
| `System` | `SystemType?` | Тип системы управления | DCS/SCS/GDS |
| `IoType` | `string?` | Тип сигнала ввода-вывода | - |
| `Controller` | `string?` | Имя контроллера | - |
| `Location` | `LocationType?` | Место установки оборудования | - |

### 2.3. Класс InstrumentTag

**Назначение:** Парсер промышленных тегов

| Свойство | Тип | Описание | Ограничения |
|:---|:---|:---|:---|
| `Area` | `string?` | Код площадки/завода | 1-6 цифр |
| `DeviceClass` | `string?` | Класс прибора | 1-5 букв A-Z |
| `Loop` | `string?` | Номер технологического контура | 1-5 цифр |
| `TagNumber` | `string?` | Порядковый номер прибора | 1-3 цифры |
| `Suffix` | `string?` | Суффикс | 0-3 символа |
| `FullTag` | `string?` | Исходный тег | - |
| `Separator` | `char?` | Определенный разделитель | - |

### 2.4. Класс Signal

**Назначение:** Технологические параметры сигнала

| Свойство | Тип | Описание |
|:---|:---|:---|
| `Service` | `string?` | Описание сигнала (на русском) |
| `ServiceEnglish` | `string?` | Описание сигнала (на английском) |
| `InstrumentType` | `string?` | Тип прибора (на русском) |
| `InstrumentTypeEnglish` | `string?` | Тип прибора (на английском) |
| `Pid` | `string?` | Номер P&ID для привязки к схеме |
| `SignalType` | `string?` | Тип сигнала |
| `ExProtection` | `ExProtectionType?` | Взрывозащита оборудования |
| `Subsystem` | `string?` | Идентификатор подсистемы |
| `Auxiliary` | `string?` | Вспомогательный прибор |

### 2.5. Класс Ranges

**Назначение:** Диапазоны измерений и уставки сигнализации

| Свойство | Тип | Описание |
|:---|:---|:---|
| `Primary` | `Range?` | Основной диапазон измерения |
| `Secondary` | `Range?` | Вторичный диапазон измерения |
| `Alarms` | `AlarmSet?` | Уставки аварийной сигнализации |

### 2.6. Класс Range

| Свойство | Тип | Описание |
|:---|:---|:---|
| `Min` | `decimal?` | Минимальное значение |
| `Max` | `decimal?` | Максимальное значение |
| `Unit` | `string?` | Единица измерения |

### 2.7. Класс AlarmSet

**Назначение:** Уставки аварийной сигнализации с иерархией LL2 < LL < L < H < HH < HH2

| Свойство | Тип | Описание |
|:---|:---|:---|
| `LL2` | `AlarmLevel?` | Дополнительный критичный нижний |
| `LL` | `AlarmLevel?` | Критичный нижний уровень |
| `L` | `AlarmLevel?` | Нижнее предупреждение |
| `H` | `AlarmLevel?` | Верхнее предупреждение |
| `HH` | `AlarmLevel?` | Критичный верхний уровень |
| `HH2` | `AlarmLevel?` | Дополнительный критичный верхний |

### 2.8. Класс AlarmLevel

| Свойство | Тип | Описание |
|:---|:---|:---|
| `Value` | `decimal?` | Значение уставки |
| `Unit` | `string?` | Единица измерения |

### 2.9. Класс Cable

**Назначение:** Кабельное хозяйство

| Свойство | Тип | Описание |
|:---|:---|:---|
| `Id` | `string?` | Уникальный идентификатор кабеля |
| `Description` | `string?` | Описание кабеля |
| `Type` | `string?` | Полный тип кабеля |
| `Designation` | `string?` | Обозначение кабеля |
| `From` | `string?` | Начальная точка кабеля |
| `To` | `string?` | Конечная точка кабеля |
| `Length` | `int?` | Длина кабеля в метрах |
| `Color` | `string?` | Цвет кабеля или изоляции жилы |
| `Pair` | `int?` | Номер пары или жилы |
| `Note` | `string?` | Дополнительное примечание |
| `VendorDesignation` | `string?` | Наименование по документации поставщика |
| `Drum` | `string?` | Номер барабана |
| `Voltage` | `string?` | Напряжение питания |

### 2.10. Класс Equipment

**Назначение:** Оборудование контроллера

| Свойство | Тип | Описание |
|:---|:---|:---|
| `CableTecon` | `string?` | Идентификатор кабеля по документации TECON |
| `Mcc` | `string?` | Номер подстанции MCC |
| `ControlCabinet` | `string?` | Идентификатор шкафа управления |
| `MarshallingCabinet` | `string?` | Идентификатор шкафа маршаллинга |
| `Cpu` | `string?` | Тег процессора (CPU) |
| `Chassis` | `Chassis?` | Шасси (стойка) контроллера |
| `Module` | `Module?` | Модуль ввода-вывода |

### 2.11. Класс Chassis

| Свойство | Тип | Описание |
|:---|:---|:---|
| `Main` | `string?` | Идентификатор основного шасси |
| `Redundant` | `string?` | Идентификатор резервного шасси |

### 2.12. Класс Module

| Свойство | Тип | Описание |
|:---|:---|:---|
| `Slot` | `string?` | Номер слота основного модуля |
| `SlotRedundant` | `string?` | Номер слота резервного модуля |
| `Channel` | `int?` | Номер канала внутри модуля |
| `Type` | `string?` | Тип модуля ввода-вывода |

### 2.13. Класс Revision

**Назначение:** Управление версиями

| Свойство | Тип | Описание |
|:---|:---|:---|
| `Number` | `int?` | Номер ревизии |
| `Description` | `string?` | Описание изменений |
| `Package` | `string?` | Оборудование поставщика |
| `VendorField1` | `string?` | Дополнительное поле 1 |
| `VendorField2` | `string?` | Дополнительное поле 2 |
| `Document` | `string?` | Ссылка на документацию |
| `AkerRevision` | `string?` | Ревизия от Aker Solutions |
| `FileName` | `string?` | Имя файла-источника |
| `Author` | `string?` | Автор записи |
| `DateEntered` | `DateTime?` | Дата и время ввода |
| `RowNumber` | `int?` | Номер строки в документе |

---

## 3. ПЕРЕЧИСЛЕНИЯ (ENUMS)

### 3.1. SystemType

| Значение | Описание |
|:---|:---|
| `DCS` | Распределенная система управления |
| `SCS` | Система безопасности / SIS |
| `GDS` | Система обнаружения газа |

### 3.2. LocationType

| Значение | Описание |
|:---|:---|
| `Field` | Полевое оборудование |
| `SIS` | Система безопасности |
| `MCC` | Шкаф мотор-контроллеров |
| `PLC` | Программируемый логический контроллер |
| `AUX` | Вспомогательное оборудование |
| `GDS` | Система обнаружения газа |

### 3.3. ExProtectionType

| Значение | Описание |
|:---|:---|
| `ExD` | Взрывонепроницаемая оболочка |
| `ExI` | Искробезопасная цепь |
| `ExE` | Повышенная защита |
| `ExN` | Неискрящее оборудование |
| `ExP` | Заполнение под давлением |
| `ExO` | Масляное заполнение |
| `ExQ` | Кварцевое заполнение |
| `ExM` | Герметизация компаундом |
| `None` | Взрывозащита не требуется |

### 3.4. IOPointStatus

| Значение | Описание |
|:---|:---|
| `Active` | Активен (штатный режим) |
| `Inactive` | Неактивен (отключен) |
| `Commissioning` | Ввод в эксплуатацию |
| `Maintenance` | На обслуживании |
| `Fault` | Ошибка (неисправность) |
| `Decommissioned` | Выведен из эксплуатации |

### 3.5. AlarmValidationSeverity

| Значение | Описание |
|:---|:---|
| `Critical` | Критическая ошибка |
| `Error` | Ошибка |
| `Warning` | Предупреждение |
| `Info` | Информация |
| `Valid` | Корректно |

### 3.6. AccessImportStatus / AccessExportStatus

| Значение | Описание |
|:---|:---|
| `NotStarted` | Не начат |
| `InProgress` | В процессе |
| `Completed` | Завершен |
| `Failed` | Ошибка |
| `Cancelled` | Отменен |

---

## 4. ИНТЕРФЕЙСЫ

### 4.1. Базовые интерфейсы

| Интерфейс | Назначение | Основные методы |
|:---|:---|:---|
| `IIOPoint` | Базовый интерфейс для всех точек | GetFullIdentifier, GetDisplayName |
| `IIdentifiable` | Идентификация объектов | GetFullIdentifier, IsValid, EqualsByTag |
| `IValidatable` | Валидация данных | Validate, ValidateAsync |
| `IVersionable` | Управление версиями | UpdateVersion, GetVersionHistory |
| `ICloneable<T>` | Клонирование объектов | DeepClone, ShallowClone |
| `IEquatable<IOPoint>` | Сравнение точек | Equals, EqualsByTag |

### 4.2. Интерфейсы валидации

| Интерфейс | Назначение | Основные методы |
|:---|:---|:---|
| `IExcelImportValidator` | Валидация данных из Excel | ValidateExcelFile, ValidateExcelRow |
| `IAccessImportValidator` | Валидация данных из Access | ValidateAccessTable, ValidateAccessData |
| `IBatchValidator` | Пакетная валидация | ValidateAll, ValidateBatch |
| `IAlarmValidator` | Валидация уставок | ValidateHierarchy, ValidateConsistency |

### 4.3. Интерфейсы репозитория

| Интерфейс | Назначение | Основные методы |
|:---|:---|:---|
| `IRepository<T>` | Базовый репозиторий | GetAll, GetById, Add, Update, Delete |
| `IIOPointRepository` | Репозиторий для IOPoint | GetByTag, GetByArea, GetBySystem, Search |
| `IUnitOfWork` | Единица работы | BeginTransaction, CommitTransaction, SaveChanges |

### 4.4. Интерфейсы для работы с Excel

| Интерфейс | Назначение | Основные методы |
|:---|:---|:---|
| `IExcelFieldMapper` | Сопоставление полей Excel | MapFields, MapRow, GetMappingPreview |
| `IExcelFieldMappingManager` | Управление маппингами | LoadMappings, SaveMappings, AutoDetectMapping |
| `IExcelPreviewProvider` | Провайдер предпросмотра | GetPreviewData, GetColumnHeaders, GetSampleData |

---

## 5. ВЛОЖЕННЫЕ КЛАССЫ

### 5.1. Валидаторы

| Класс | Назначение | Основные методы |
|:---|:---|:---|
| `ExcelValidator` | Валидация данных из Excel | ValidateRow, ValidateStructure, ValidateDataTypes |
| `AccessValidator` | Валидация данных из Access | ValidateTableStructure, ValidateData, ValidateIntegrity |
| `AlarmValidator` | Валидация уставок | ValidateHierarchy, ValidateConsistency, AutoFixHierarchy |

### 5.2. Компоненты работы с Excel

| Класс | Назначение | Основные методы |
|:---|:---|:---|
| `ExcelFieldMappingManager` | Управление маппингами | LoadMappings, SaveMappings, AutoDetectMapping |
| `ExcelFieldMapper` | Маппер полей | MapFields, MapRow, GetMappedValue |
| `ExcelMappingManager` | Менеджер сопоставления | AnalyzeExcelStructure, SuggestMappings, ApplyMapping |
| `ExcelPreviewProvider` | Провайдер предпросмотра | GetPreviewData, GetMappedPreview, GetSampleData |
| `MappingTemplateManager` | Менеджер шаблонов | CreateTemplate, LoadTemplate, SaveTemplate |
| `ExcelAnalyzer` | Анализатор Excel | AnalyzeFile, AnalyzeStructure, DetectPatterns |
| `MappingRecommender` | Рекомендатель маппинга | GetRecommendations, GetBestMatch, GetSmartSuggestions |

### 5.3. Классы данных маппинга

| Класс | Назначение | Свойства |
|:---|:---|:---|
| `ExcelFieldMapping` | Сопоставление поля | IOPointField, ExcelColumn, Confidence, Converter |
| `ExcelMappingTemplate` | Шаблон маппинга | Name, Description, Mappings, Version |
| `ExcelMappingResult` | Результат маппинга | IsValid, Mappings, Errors, Confidence |
| `ExcelMappingSuggestion` | Предложение маппинга | IOPointField, ExcelColumn, Confidence, Reason |
| `ExcelStructureInfo` | Информация о структуре | TotalRows, TotalColumns, HeaderRow, Columns |
| `ExcelColumnInfo` | Информация о колонке | Name, Index, DataType, SampleValues |

### 5.4. Конвертеры данных

| Класс | Назначение |
|:---|:---|
| `IDataConverter` | Базовый интерфейс конвертера |
| `StringToIntConverter` | Преобразование строки в целое число |
| `StringToDecimalConverter` | Преобразование строки в десятичное число |
| `StringToDateTimeConverter` | Преобразование строки в дату/время |
| `StringToEnumConverter` | Преобразование строки в перечисление |
| `StringToBoolConverter` | Преобразование строки в булево значение |
| `StringToTagConverter` | Преобразование строки в формат Tag |
| `CustomConverter` | Пользовательский конвертер |
| `CompositeConverter` | Составной конвертер |

### 5.5. Классы результатов

| Класс | Назначение | Свойства |
|:---|:---|:---|
| `ExcelValidationResult` | Результат проверки Excel | IsValid, Errors, Warnings, DataQualityScore |
| `AccessValidationResult` | Результат проверки Access | IsValid, Errors, Warnings, TotalRecords |
| `AlarmValidationResult` | Результат проверки уставок | IsValid, Errors, Warnings, SuggestedFixes |
| `DataQualityReport` | Отчет о качестве данных | OverallScore, Completeness, Accuracy, Consistency |
| `PagedResult<T>` | Результат с пагинацией | Items, TotalCount, PageNumber, TotalPages |
| `ImportResult` | Результат импорта | ImportedCount, ErrorCount, Errors, ImportedPoints |
| `AccessImportResult` | Результат импорта Access | ExportedCount, UpdatedCount, ErrorCount, TableName |
| `ValidationError` | Ошибка валидации | Property, Message, InvalidValue, Timestamp |
| `SearchCriteria` | Критерии поиска | SearchTerm, Area, System, Status, DateFrom, DateTo |

### 5.6. Вспомогательные классы

| Класс | Назначение | Основные методы |
|:---|:---|:---|
| `TagManager` | Управление уникальностью Tag | ValidateTag, GenerateTag, RegisterTag, UnregisterTag |
| `RevisionHistory` | История изменений | Add, GetLastEntry, GetEntriesByAuthor |
| `Monitor` | Мониторинг состояния | StartMonitoring, StopMonitoring, CheckStatus |
| `PidReference` | Ссылка на P&ID | GetPidUrl, GetPosition, GetConnectedTo |
| `PointFilterBuilder` | Построитель фильтров | AddAreaFilter, AddSystemFilter, AddStatusFilter, Build |

---

## 6. КОМПОНЕНТЫ РАБОТЫ С ДАННЫМИ

### 6.1. Репозитории

| Компонент | Назначение | Основные методы |
|:---|:---|:---|
| `IRepository<T>` | Базовый репозиторий | GetAll, GetById, Add, Update, Delete, SaveChanges |
| `IOPointRepository` | Репозиторий IOPoint | GetByTag, GetByArea, GetBySystem, Search, GetStatistics |
| `CachedRepository<T>` | Кэширующий репозиторий | GetCached, AddToCache, InvalidateCache, GetCacheStats |
| `AccessRepository` | Репозиторий Access | GetPoints, GetPointByTag, AddPoint, UpdatePoint, DeletePoint |

### 6.2. Сервисы

| Компонент | Назначение | Основные методы |
|:---|:---|:---|
| `BaseService<T>` | Базовый сервис | Create, Update, Delete, GetById, Validate |
| `IOPointService` | Сервис IOPoint | ProcessBatch, ImportData, ExportData, GenerateReport |
| `UnitOfWork` | Единица работы | BeginTransaction, CommitTransaction, RollbackTransaction, SaveChanges |

### 6.3. Провайдеры данных

| Компонент | Назначение | Основные методы |
|:---|:---|:---|
| `AccessDataProvider` | Провайдер данных Access | Connect, Disconnect, ExecuteQuery, GetTableData |
| `ExcelPreviewProvider` | Провайдер предпросмотра Excel | GetPreviewData, GetColumnHeaders, GetSampleData |

### 6.4. Импортеры/Экспортеры

| Компонент | Назначение | Основные методы |
|:---|:---|:---|
| `AccessImporter` | Импортер из Access | ImportFromAccess, ImportTable, ImportQuery, ImportWithValidation |
| `AccessExporter` | Экспортер в Access | ExportToAccess, ExportToTable, ExportWithSchema, ExportIncremental |
| `ExcelFieldMapper` | Маппер полей Excel | MapFields, MapRow, GetMappedValue, ValidateMapping |
| `ExcelMappingManager` | Менеджер сопоставления | AnalyzeExcelStructure, SuggestMappings, ApplyMapping |

---

## 7. СОБЫТИЯ

### 7.1. События состояния

| Событие | Описание |
|:---|:---|
| `ValueChanged` | Изменение значения сигнала |
| `StatusChanged` | Изменение статуса точки |
| `TagChanged` | Изменение Tag |
| `DataQualityChanged` | Изменение качества данных |
| `RevisionChanged` | Обновление ревизии |

### 7.2. События валидации

| Событие | Описание |
|:---|:---|
| `AlarmValidated` | Проверка уставок |
| `AlarmValidationError` | Ошибка в иерархии уставок |
| `ExcelValidationStarted` | Начало проверки Excel |
| `ExcelValidationCompleted` | Завершение проверки Excel |
| `ExcelValidationError` | Ошибка проверки Excel |
| `ExcelValidationWarning` | Предупреждение проверки Excel |
| `AccessValidationStarted` | Начало проверки Access |
| `AccessValidationCompleted` | Завершение проверки Access |
| `AccessValidationError` | Ошибка проверки Access |
| `AccessValidationWarning` | Предупреждение проверки Access |
| `ValidationError` | Ошибка валидации |

### 7.3. События маппинга

| Событие | Описание |
|:---|:---|
| `ExcelMappingStarted` | Начало маппинга Excel |
| `ExcelMappingCompleted` | Завершение маппинга Excel |
| `ExcelMappingProgress` | Прогресс маппинга |
| `ExcelAutoDetectCompleted` | Автодетектирование завершено |

### 7.4. События импорта/экспорта

| Событие | Описание |
|:---|:---|
| `AccessImportStarted` | Начало импорта из Access |
| `AccessImportCompleted` | Завершение импорта из Access |
| `AccessExportStarted` | Начало экспорта в Access |
| `AccessExportCompleted` | Завершение экспорта в Access |
| `AccessConnectionChanged` | Изменение подключения к Access |

### 7.5. События жизненного цикла

| Событие | Описание |
|:---|:---|
| `PointCreated` | Создание точки |
| `PointUpdated` | Обновление точки |
| `PointDeleted` | Удаление точки |
| `PointRestored` | Восстановление точки |

---

## 8. ОПЕРАТОРЫ

| Оператор | Описание |
|:---|:---|
| `== / != (IOPoint, IOPoint)` | Сравнение точек по Tag |
| `== / != (IOPoint, string)` | Сравнение точки с тегом |
| `== / != (string, IOPoint)` | Сравнение тега с точкой |
| `implicit operator string` | Неявное преобразование в строку (Tag) |
| `explicit operator IOPoint` | Явное преобразование из строки (Tag) |
| `+ (IOPoint, IOPoint)` | Объединение данных двух точек |

---

## 9. ИНДЕКСАТОРЫ

| Индексатор | Описание |
|:---|:---|
| `this[string propertyName]` | Доступ к свойствам по имени |
| `this[string tag, bool useCache]` | Поиск по тегу с кэшированием |

---

## 10. ДЕЛЕГАТЫ

| Делегат | Описание |
|:---|:---|
| `ValueChangedHandler` | Обработчик изменения значения |
| `AlarmHandler` | Обработчик аварийных событий |
| `AlarmValidationHandler` | Обработчик проверки уставок |
| `ExcelValidationHandler` | Обработчик проверки Excel |
| `ExcelErrorHandler` | Обработчик ошибок Excel |
| `ExcelMappingHandler` | Обработчик маппинга Excel |
| `ExcelMappingProgressHandler` | Обработчик прогресса маппинга |
| `AccessValidationHandler` | Обработчик проверки Access |
| `AccessErrorHandler` | Обработчик ошибок Access |
| `AccessImportHandler` | Обработчик импорта из Access |
| `AccessExportHandler` | Обработчик экспорта в Access |
| `TagValidationHandler` | Обработчик валидации Tag |
| `ValidationHandler` | Обработчик ошибок валидации |
| `PointCreatedHandler` | Обработчик создания точки |
| `PointUpdatedHandler` | Обработчик обновления точки |
| `PointDeletedHandler` | Обработчик удаления точки |
| `PointFilter` | Фильтр для точек |
| `PointAction` | Действие над точкой |
| `PointTransformer` | Трансформатор точки |

---

## 11. МЕТОДЫ

### 11.1. Основные методы

| Метод | Описание |
|:---|:---|
| `UpdateValue(decimal newValue)` | Обновление текущего значения |
| `SetTag(string newTag)` | Установка Tag с проверкой уникальности |
| `GetFullIdentifier()` | Получение полного идентификатора |
| `GetDisplayName()` | Получение отображаемого имени |
| `GetSummary()` | Получение краткого описания |
| `HasCompleteData()` | Проверка наличия всех данных |
| `IsActive()` | Проверка активности точки |
| `MarkAsDeleted()` | Пометить как удаленную |
| `Restore()` | Восстановить из архива |
| `Archive()` | Архивировать |

### 11.2. Методы валидации

| Метод | Описание |
|:---|:---|
| `Validate(out List<ValidationError> errors)` | Основная проверка данных |
| `ValidateTag()` | Проверка уникальности и формата Tag |
| `ValidateIdentification()` | Валидация идентификационных данных |
| `ValidateSignal()` | Валидация параметров сигнала |
| `ValidateRanges()` | Валидация диапазонов и уставок |
| `ValidateAlarms()` | Валидация иерархии уставок |
| `ValidateCable()` | Валидация кабельных данных |
| `ValidateEquipment()` | Валидация аппаратной привязки |
| `ValidateFromExcel()` | Проверка данных из Excel |
| `ValidateExcelRow()` | Проверка отдельной строки Excel |

### 11.3. Методы маппинга Excel

| Метод | Описание |
|:---|:---|
| `AnalyzeExcelFile()` | Анализ структуры Excel файла |
| `GetExcelHeaders()` | Получение заголовков Excel |
| `GetExcelStructure()` | Получение структуры Excel |
| `AutoDetectMapping()` | Автоматическое обнаружение маппинга |
| `SuggestMapping()` | Предложение маппинга |
| `ApplyMapping()` | Применение маппинга |
| `ValidateMapping()` | Проверка маппинга |
| `GetMappingPreview()` | Предпросмотр маппинга |
| `SaveMappingTemplate()` | Сохранение шаблона маппинга |
| `LoadMappingTemplate()` | Загрузка шаблона маппинга |

### 11.4. Методы импорта/экспорта

| Метод | Описание |
|:---|:---|
| `ConnectToAccess()` | Подключение к Access |
| `DisconnectFromAccess()` | Отключение от Access |
| `ImportFromAccess()` | Импорт из Access |
| `ExportToAccess()` | Экспорт в Access |
| `ImportFromExcel()` | Импорт из Excel |
| `ExportToExcel()` | Экспорт в Excel |

### 11.5. Методы проверки уставок

| Метод | Описание |
|:---|:---|
| `CheckAlarmHierarchy()` | Проверка иерархии LL2 < LL < L < H < HH < HH2 |
| `CheckAlarmConsistency()` | Проверка согласованности уставок |
| `CheckAlarmUnits()` | Проверка единиц измерения уставок |
| `CheckAlarmWithRange()` | Проверка уставок относительно диапазона |
| `ValidateAllAlarms()` | Комплексная проверка всех уставок |
| `FixAlarmHierarchy()` | Автоматическое исправление иерархии |

### 11.6. Методы управления качеством данных

| Метод | Описание |
|:---|:---|
| `CalculateDataQualityScore()` | Расчет оценки качества данных |
| `GetDataQualityReport()` | Получение отчета о качестве |
| `GetMissingDataFields()` | Получение списка отсутствующих данных |
| `GetInvalidDataFields()` | Получение списка некорректных данных |
| `GetDataCompleteness()` | Получение полноты данных |
| `GetDataAccuracy()` | Получение точности данных |
| `GetDataConsistency()` | Получение согласованности данных |

### 11.7. Асинхронные методы

| Метод | Описание |
|:---|:---|
| `ValidateAsync()` | Асинхронная валидация |
| `ValidateExcelAsync()` | Асинхронная проверка Excel |
| `AnalyzeExcelAsync()` | Асинхронный анализ Excel |
| `AutoDetectMappingAsync()` | Асинхронное автодетектирование |
| `ImportFromAccessAsync()` | Асинхронный импорт из Access |
| `ExportToAccessAsync()` | Асинхронный экспорт в Access |
| `SaveAsync()` | Асинхронное сохранение в БД |
| `LoadAsync(string tag)` | Асинхронная загрузка из БД |

### 11.8. Функциональные методы

| Метод | Описание |
|:---|:---|
| `If(Func<IOPoint, bool> condition, Action<IOPoint> action)` | Условное выполнение |
| `Map(Func<IOPoint, IOPoint> transform)` | Трансформация |
| `Match<T>(Func<IOPoint, T> onValid, Func<IOPoint, T> onInvalid)` | Сопоставление |
| `Apply(params Action<IOPoint>[] actions)` | Применение набора действий |

---

## 12. СТАТИЧЕСКИЕ МЕТОДЫ

### 12.1. Фабричные методы

| Метод | Описание |
|:---|:---|
| `CreateNew(string tag, string service)` | Создание новой точки |
| `CreateFromParsedTag(InstrumentTag parsedTag)` | Создание из распарсенного тега |
| `FromCsvRow(Dictionary<string, string> row)` | Создание из строки CSV |
| `FromJson(string json)` | Создание из JSON |
| `FromAccessRow(AccessRowData rowData)` | Создание из строки Access |

### 12.2. Методы управления Tag

| Метод | Описание |
|:---|:---|
| `RegisterTag(string tag)` | Регистрация Tag |
| `UnregisterTag(string tag)` | Удаление Tag |
| `IsTagRegistered(string tag)` | Проверка регистрации |
| `ValidateTagUniqueness(string tag)` | Проверка уникальности |
| `GenerateUniqueTag(string baseTag)` | Генерация уникального Tag |
| `GetAllRegisteredTags()` | Получение всех зарегистрированных Tag |

### 12.3. Методы работы с репозиторием

| Метод | Описание |
|:---|:---|
| `GetRepository<T>()` | Получение репозитория |
| `RegisterRepository<T>(IRepository<T> repository)` | Регистрация репозитория |
| `GetService<T>()` | Получение сервиса |
| `RegisterService<T>(T service)` | Регистрация сервиса |
| `CreateUnitOfWork()` | Создание единицы работы |
| `BeginTransaction()` | Начало транзакции |
| `CommitTransaction()` | Фиксация транзакции |
| `RollbackTransaction()` | Откат транзакции |

### 12.4. Методы маппинга Excel

| Метод | Описание |
|:---|:---|
| `AnalyzeExcelFile(string filePath)` | Анализ структуры Excel файла |
| `GetExcelStructureInfo(string filePath)` | Получение информации о структуре |
| `AutoDetectMapping(string filePath)` | Автоматическое обнаружение маппинга |
| `SuggestMapping(string filePath)` | Предложение маппинга |
| `ApplyMapping(string filePath, ExcelMappingTemplate template)` | Применение маппинга |
| `ValidateMapping(ExcelMappingTemplate template)` | Проверка маппинга |
| `SaveMappingTemplate(ExcelMappingTemplate template)` | Сохранение шаблона маппинга |
| `LoadMappingTemplate(string name)` | Загрузка шаблона маппинга |
| `GetMappingTemplates()` | Получение всех шаблонов |
| `DeleteMappingTemplate(string name)` | Удаление шаблона |

### 12.5. Методы кэширования

| Метод | Описание |
|:---|:---|
| `GetCached(string tag)` | Получение из кэша |
| `CachePoint(IOPoint point)` | Добавление в кэш |
| `RemoveFromCache(string tag)` | Удаление из кэша |
| `ClearCache()` | Очистка кэша |
| `InvalidateCache()` | Инвалидация кэша |
| `GetCacheStats()` | Статистика кэша |

---

## 13. МЕТОДЫ РАСШИРЕНИЯ

### 13.1. Фильтрация

| Метод | Описание |
|:---|:---|
| `FilterBySystem()` | По системе управления |
| `FilterByLocation()` | По месту установки |
| `FilterByStatus()` | По статусу |
| `FilterByArea()` | По зоне |
| `FilterByService()` | По описанию |
| `FilterByValidAlarms()` | По корректности уставок |
| `FilterByDataQuality()` | По качеству данных |
| `FilterByValidationStatus()` | По статусу валидации |
| `FilterByImportSource()` | По источнику импорта |
| `FilterByDateRange()` | По диапазону дат |
| `FilterByController()` | По контроллеру |
| `FilterByIds()` | По списку идентификаторов |
| `FilterByMappingConfidence()` | По уверенности маппинга |

### 13.2. Группировка

| Метод | Описание |
|:---|:---|
| `GroupBySystem()` | По системе управления |
| `GroupByArea()` | По зоне |
| `GroupByStatus()` | По статусу |
| `GroupByAlarmValidity()` | По корректности уставок |
| `GroupByDataQualityLevel()` | По уровню качества |
| `GroupByValidationStatus()` | По статусу валидации |
| `GroupByMappingConfidence()` | По уверенности маппинга |

### 13.3. Поиск

| Метод | Описание |
|:---|:---|
| `FindByTag()` | По уникальному тегу |
| `FindAllByService()` | По описанию |
| `FindByTagPattern()` | По шаблону Tag |
| `FindWithInvalidAlarms()` | С некорректными уставками |
| `FindWithDataQualityIssues()` | С проблемами качества |
| `FindByImportSource()` | По источнику импорта |
| `FindByIds()` | По списку идентификаторов |
| `FindByDateRange()` | По диапазону дат |

### 13.4. Сортировка

| Метод | Описание |
|:---|:---|
| `SortByTag()` | По тегу |
| `SortByArea()` | По зоне |
| `SortByDataQuality()` | По качеству данных |
| `SortByCreatedAt()` | По дате создания |
| `SortByModifiedAt()` | По дате изменения |
| `SortByStatus()` | По статусу |
| `SortByMappingConfidence()` | По уверенности маппинга |

### 13.5. Пагинация

| Метод | Описание |
|:---|:---|
| `ToPagedResult()` | Преобразование в результат с пагинацией |
| `GetPage()` | Получение страницы |
| `GetFirstPage()` | Получение первой страницы |
| `GetLastPage()` | Получение последней страницы |
| `GetNextPage()` | Получение следующей страницы |
| `GetPreviousPage()` | Получение предыдущей страницы |

### 13.6. Экспорт

| Метод | Описание |
|:---|:---|
| `ExportToCsv()` | В CSV формат |
| `ExportToJson()` | В JSON формат |
| `ExportToExcel()` | В Excel формат |
| `ExportToAccess()` | В Access формат |
| `ExportAlarmValidationReport()` | Отчет по уставкам |
| `ExportDataQualityReport()` | Отчет по качеству |
| `ExportValidationReport()` | Отчет по валидации |
| `ExportMappingReport()` | Отчет по маппингу |

### 13.7. Пакетные операции

| Метод | Описание |
|:---|:---|
| `BatchUpdate()` | Параллельное обновление |
| `ForEachPoint()` | Последовательная обработка |
| `ValidateAllAlarms()` | Проверка уставок всех точек |
| `FixAllAlarmHierarchies()` | Исправление всех уставок |
| `ValidateAllExcelImports()` | Проверка всех импортов |
| `CalculateAllDataQuality()` | Расчет качества всех точек |
| `ApplyMappingToAll()` | Применение маппинга ко всем |
| `AutoDetectMappingForAll()` | Автодетект маппинга для всех |

---

## 14. АТРИБУТЫ

| Атрибут | Назначение |
|:---|:---|
| `[Serializable]` | Поддержка бинарной сериализации |
| `[JsonConverter]` | Конвертер для JSON |
| `[DataContract]` | Контракт для WCF |
| `[Table("IOPoints")]` | Маппинг для базы данных |
| `[Index(nameof(Tag), IsUnique = true)]` | Уникальный индекс в БД |
| `[Index(nameof(Id))]` | Индекс по идентификатору |
| `[JsonIgnore]` | Игнорирование при JSON сериализации |
| `[XmlIgnore]` | Игнорирование при XML сериализации |
| `[NotMapped]` | Игнорирование при маппинге в БД |
| `[Required]` | Обязательность Tag |

---

## 15. КОНСТАНТЫ

| Константа | Значение | Описание |
|:---|:---|:---|
| `MaxAreaLength` | 6 | Максимальная длина Area |
| `MinTagNumberLength` | 1 | Минимальная длина TagNumber |
| `MaxTagNumberLength` | 3 | Максимальная длина TagNumber |
| `MaxDeviceClassLength` | 5 | Максимальная длина DeviceClass |
| `MinTagLength` | 3 | Минимальная длина Tag |
| `MaxTagLength` | 50 | Максимальная длина Tag |
| `TagPattern` | ^[A-Z0-9-]+$ | Шаблон для валидации Tag |
| `AlarmHierarchy` | LL2 < LL < L < H < HH < HH2 | Правило иерархии уставок |
| `DefaultUnit` | % | Единица измерения по умолчанию |
| `MaxImportFileSizeMB` | 100 | Максимальный размер файла импорта |
| `SupportedExcelFormats` | .xlsx, .xls | Поддерживаемые форматы Excel |
| `SupportedAccessFormats` | .accdb, .mdb | Поддерживаемые форматы Access |
| `DefaultPageSize` | 50 | Размер страницы по умолчанию |
| `MaxPageSize` | 1000 | Максимальный размер страницы |
| `MinMappingConfidence` | 50 | Минимальная уверенность маппинга |
| `MaxRowsForPreview` | 100 | Максимальное строк для предпросмотра |
| `AutoDetectThreshold` | 70 | Порог автодетектирования |

---

## 16. ТРЕБОВАНИЯ К ФУНКЦИОНАЛЬНОСТИ

### 16.1. Уникальность Tag
- Tag должен быть уникальным в системе
- При создании/изменении Tag должна выполняться проверка уникальности
- При конфликте должна генерироваться ошибка или автоматически создаваться уникальный Tag

### 16.2. Валидация данных
- Должна выполняться валидация всех компонентов точки
- Проверка обязательных полей
- Проверка форматов данных (Area, TagNumber, DeviceClass)
- Проверка иерархии уставок LL2 < LL < L < H < HH < HH2

### 16.3. Маппинг Excel
- Автоматическое обнаружение маппинга полей
- Ручная настройка маппинга
- Сохранение шаблонов маппинга
- Предпросмотр результатов маппинга
- Оценка уверенности маппинга

### 16.4. Импорт/Экспорт
- Поддержка импорта из Excel (.xlsx, .xls)
- Поддержка импорта из Access (.accdb, .mdb)
- Поддержка экспорта в Excel и Access
- Валидация данных перед импортом

### 16.5. Репозиторий
- CRUD операции для работы с точками
- Поиск по различным критериям
- Пагинация результатов
- Кэширование для оптимизации

### 16.6. Событийная модель
- Оповещение об изменениях состояния
- Уведомления об ошибках валидации
- Прогресс импорта/экспорта
- Результаты маппинга

---

## 17. ТРЕБОВАНИЯ К КАЧЕСТВУ КОДА

### 17.1. Архитектура
- Четкое разделение ответственности
- Использование интерфейсов для слабой связанности
- Поддержка Dependency Injection
- Применение паттернов Repository, Unit of Work, Factory

### 17.2. Обработка ошибок
- Все методы должны обрабатывать исключения
- Должна быть система валидации с детальными ошибками
- Логирование всех критических операций

### 17.3. Производительность
- Кэширование часто используемых данных
- Асинхронные операции для длительных задач
- Оптимизация работы с большими объемами данных

### 17.4. Тестирование
- Unit тесты для всех компонентов
- Интеграционные тесты для работы с репозиторием
- Тесты валидации и маппинга

---

## 18. ДОКУМЕНТАЦИЯ

### 18.1. XML комментарии
- Все публичные методы должны иметь XML комментарии
- Описание параметров и возвращаемых значений
- Примеры использования

### 18.2. README
- Общее описание класса
- Примеры использования
- Инструкция по настройке

---

## 19. КРИТЕРИИ ПРИЕМКИ

### 19.1. Функциональные
- [ ] Все классы и свойства реализованы согласно спецификации
- [ ] Уникальность Tag обеспечена
- [ ] Валидация всех данных работает корректно
- [ ] Иерархия уставок проверяется правильно
- [ ] Маппинг Excel работает с автодетектом и ручной настройкой
- [ ] Импорт/Экспорт в Excel и Access работает
- [ ] Репозиторий обеспечивает все CRUD операции
- [ ] События генерируются при всех изменениях

### 19.2. Качество
- [ ] Код соответствует стандартам C#
- [ ] Все методы покрыты XML комментариями
- [ ] Unit тесты покрывают >80% кода
- [ ] Обработка ошибок реализована для всех операций
- [ ] Асинхронные методы реализованы корректно

### 19.3. Производительность
- [ ] Кэширование работает эффективно
- [ ] Загрузка 10,000 точек не превышает 5 секунд
- [ ] Импорт 10,000 строк из Excel не превышает 30 секунд

---

## 20. ИТОГОВЫЙ ЧЕК-ЛИСТ РАЗРАБОТКИ

### Базовые классы (6)
- [ ] IOPoint
- [ ] Identification
- [ ] InstrumentTag
- [ ] Signal
- [ ] Ranges (с Range, AlarmSet, AlarmLevel)
- [ ] Cable
- [ ] Equipment (с Chassis, Module)
- [ ] Revision

### Перечисления (6)
- [ ] SystemType
- [ ] LocationType
- [ ] ExProtectionType
- [ ] IOPointStatus
- [ ] AlarmValidationSeverity
- [ ] AccessImportStatus / AccessExportStatus

### Интерфейсы (16+)
- [ ] Базовые: IIOPoint, IIdentifiable, IValidatable, IVersionable, ICloneable, IEquatable
- [ ] Валидации: IExcelImportValidator, IAccessImportValidator, IBatchValidator, IAlarmValidator
- [ ] Репозитория: IRepository, IIOPointRepository, IUnitOfWork
- [ ] Excel: IExcelFieldMapper, IExcelFieldMappingManager, IExcelPreviewProvider

### Вложенные классы (25+)
- [ ] ExcelValidator, AccessValidator, AlarmValidator
- [ ] ExcelFieldMappingManager, ExcelFieldMapper, ExcelMappingManager
- [ ] ExcelPreviewProvider, MappingTemplateManager, ExcelAnalyzer, MappingRecommender
- [ ] ExcelFieldMapping, ExcelMappingTemplate, ExcelMappingResult, ExcelMappingSuggestion
- [ ] ExcelStructureInfo, ExcelColumnInfo
- [ ] Конвертеры: StringToInt, StringToDecimal, StringToDateTime, StringToEnum, StringToBool, StringToTag
- [ ] ExcelValidationResult, AccessValidationResult, AlarmValidationResult
- [ ] DataQualityReport, PagedResult, ImportResult, ValidationError, SearchCriteria
- [ ] TagManager, RevisionHistory, Monitor, PidReference, PointFilterBuilder

### Репозитории и Сервисы
- [ ] IRepository<T>
- [ ] IOPointRepository
- [ ] CachedRepository<T>
- [ ] AccessRepository
- [ ] BaseService<T>
- [ ] IOPointService
- [ ] UnitOfWork

### Провайдеры и Импортеры/Экспортеры
- [ ] AccessDataProvider
- [ ] ExcelPreviewProvider
- [ ] AccessImporter
- [ ] AccessExporter
- [ ] ExcelFieldMapper
- [ ] ExcelMappingManager

### События (28)
- [ ] Состояния: ValueChanged, StatusChanged, TagChanged, DataQualityChanged, RevisionChanged
- [ ] Валидации: AlarmValidated, AlarmValidationError, ExcelValidationStarted/Completed/Error/Warning, AccessValidationStarted/Completed/Error/Warning, ValidationError
- [ ] Маппинга: ExcelMappingStarted/Completed/Progress, ExcelAutoDetectCompleted
- [ ] Импорта/Экспорта: AccessImportStarted/Completed, AccessExportStarted/Completed, AccessConnectionChanged
- [ ] Жизненного цикла: PointCreated/Updated/Deleted/Restored

### Операторы и Индексаторы
- [ ] Операторы: ==, != (3 варианта), implicit, explicit, +
- [ ] Индексаторы: this[string], this[string, bool]

### Делегаты (17)
- [ ] ValueChangedHandler, AlarmHandler, AlarmValidationHandler
- [ ] ExcelValidationHandler, ExcelErrorHandler, ExcelMappingHandler, ExcelMappingProgressHandler
- [ ] AccessValidationHandler, AccessErrorHandler, AccessImportHandler, AccessExportHandler
- [ ] TagValidationHandler, ValidationHandler
- [ ] PointCreatedHandler, PointUpdatedHandler, PointDeletedHandler
- [ ] PointFilter, PointAction, PointTransformer

### Методы расширения (60+)
- [ ] Фильтрация (14 методов)
- [ ] Группировка (8 методов)
- [ ] Поиск (10 методов)
- [ ] Сортировка (8 методов)
- [ ] Пагинация (6 методов)
- [ ] Экспорт (9 методов)
- [ ] Статистика (7 методов)
- [ ] Пакетные операции (9 методов)