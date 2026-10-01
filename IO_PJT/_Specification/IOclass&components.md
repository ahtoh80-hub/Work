# Полная структура класса IOPoint
## Со всеми компонентами и описанием

---

## 📋 ОСНОВНАЯ СТРУКТУРА КЛАССА

```
IOPoint - Точка ввода-вывода в системе автоматизации
│
├── 🔹 ИДЕНТИФИКАЦИОННЫЕ ДАННЫЕ (Identification)
│   ├── Code - Порядковый номер строки в списке
│   ├── Area - Номер технологической установки/зоны (1-6 цифр)
│   ├── Title - Номер установки/зоны (функциональное наименование)
│   ├── MainLoop - Основной технологический контур
│   ├── SubLoop - Вложенный субконтур
│   ├── ProcessLoop - Технологический контур
│   ├── Tag - УНИКАЛЬНЫЙ тег устройства (главный идентификатор)
│   ├── ParsedTag - Распарсенный тег (структурированные компоненты)
│   │   ├── Area - Код площадки/завода (1-6 цифр)
│   │   ├── DeviceClass - Класс прибора (1-5 букв A-Z)
│   │   ├── Loop - Номер технологического контура (1-5 цифр)
│   │   ├── TagNumber - Порядковый номер прибора (1-3 цифры)
│   │   ├── Suffix - Суффикс (0-3 символа)
│   │   ├── FullTag - Исходный тег в полном виде
│   │   └── Separator - Определенный разделитель
│   ├── ProcessTag - Технологический тег устройства
│   ├── System - Тип системы управления (DCS/SCS/GDS)
│   ├── IoType - Тип сигнала ввода-вывода
│   ├── Controller - Имя контроллера
│   └── Location - Место установки оборудования
│
├── 🔹 ТЕХНОЛОГИЧЕСКИЕ ПАРАМЕТРЫ СИГНАЛА (Signal)
│   ├── Service - Описание сигнала (на русском языке)
│   ├── ServiceEnglish - Описание сигнала (на английском языке)
│   ├── InstrumentType - Тип прибора (на русском языке)
│   ├── InstrumentTypeEnglish - Тип прибора (на английском языке)
│   ├── Pid - Номер P&ID для привязки к схеме
│   ├── SignalType - Тип сигнала (DOR-P, DIR(I)-NAMUR, AIR-LP)
│   ├── ExProtection - Взрывозащита оборудования (ExD, ExI, ExE и др.)
│   ├── Subsystem - Идентификатор подсистемы
│   └── Auxiliary - Вспомогательный прибор
│
├── 🔹 ДИАПАЗОНЫ И УСТАВКИ (Ranges)
│   ├── Primary - Основной диапазон измерения
│   │   ├── Min - Минимальное значение
│   │   ├── Max - Максимальное значение
│   │   └── Unit - Единица измерения
│   ├── Secondary - Вторичный диапазон измерения
│   │   ├── Min - Минимальное значение
│   │   ├── Max - Максимальное значение
│   │   └── Unit - Единица измерения
│   └── Alarms - Уставки аварийной сигнализации
│       ├── LL - Критичный нижний уровень (LL2 < LL < L < H < HH < HH2)
│       │   ├── Value - Значение уставки
│       │   └── Unit - Единица измерения
│       ├── LL2 - Дополнительный критичный нижний уровень
│       │   ├── Value - Значение уставки
│       │   └── Unit - Единица измерения
│       ├── L - Нижнее предупреждение
│       │   ├── Value - Значение уставки
│       │   └── Unit - Единица измерения
│       ├── H - Верхнее предупреждение
│       │   ├── Value - Значение уставки
│       │   └── Unit - Единица измерения
│       ├── HH - Критичный верхний уровень
│       │   ├── Value - Значение уставки
│       │   └── Unit - Единица измерения
│       └── HH2 - Дополнительный критичный верхний уровень
│           ├── Value - Значение уставки
│           └── Unit - Единица измерения
│
├── 🔹 КАБЕЛЬНОЕ ХОЗЯЙСТВО (Cable)
│   ├── Id - Уникальный идентификатор кабеля
│   ├── Description - Описание кабеля
│   ├── Type - Полный тип кабеля
│   ├── Designation - Обозначение кабеля
│   ├── From - Начальная точка кабеля
│   ├── To - Конечная точка кабеля
│   ├── Length - Длина кабеля в метрах
│   ├── Color - Цвет кабеля или изоляции жилы
│   ├── Pair - Номер пары или жилы
│   ├── Note - Дополнительное примечание
│   ├── VendorDesignation - Наименование по документации поставщика
│   ├── Drum - Номер барабана
│   └── Voltage - Напряжение питания
│
├── 🔹 ОБОРУДОВАНИЕ КОНТРОЛЛЕРА (Equipment)
│   ├── CableTecon - Идентификатор кабеля по документации TECON
│   ├── Mcc - Номер подстанции MCC
│   ├── ControlCabinet - Идентификатор шкафа управления
│   ├── MarshallingCabinet - Идентификатор шкафа маршаллинга
│   ├── Cpu - Тег процессора (CPU)
│   ├── Chassis - Шасси (стойка) контроллера
│   │   ├── Main - Идентификатор основного шасси
│   │   └── Redundant - Идентификатор резервного шасси
│   └── Module - Модуль ввода-вывода
│       ├── Slot - Номер слота основного модуля
│       ├── SlotRedundant - Номер слота резервного модуля
│       ├── Channel - Номер канала внутри модуля
│       └── Type - Тип модуля ввода-вывода
│
├── 🔹 УПРАВЛЕНИЕ ВЕРСИЯМИ (Revision)
│   ├── Number - Номер ревизии
│   ├── Description - Описание изменений
│   ├── Package - Оборудование поставщика
│   ├── VendorField1 - Дополнительное поле 1
│   ├── VendorField2 - Дополнительное поле 2
│   ├── Document - Ссылка на документацию
│   ├── AkerRevision - Ревизия от Aker Solutions
│   ├── FileName - Имя файла-источника
│   ├── Author - Автор записи
│   ├── DateEntered - Дата и время ввода
│   └── RowNumber - Номер строки в документе
│
├── 🔹 СИСТЕМНЫЕ ПОЛЯ (System Fields)
│   ├── Id - Уникальный идентификатор (GUID)
│   ├── Status - Текущий статус точки (Active, Inactive, Fault и др.)
│   ├── CurrentValue - Текущее значение сигнала
│   ├── LastUpdate - Время последнего обновления
│   ├── CreatedAt - Дата создания
│   ├── ModifiedAt - Дата последнего изменения
│   ├── DeletedAt - Дата удаления
│   ├── IsDeleted - Флаг удаления
│   ├── Version - Версия записи
│   ├── IsValid - Флаг валидности всех данных
│   ├── IsTagUnique - Флаг уникальности Tag
│   ├── TagValidationMessage - Сообщение о валидации Tag
│   ├── AreAlarmsValid - Флаг корректности уставок
│   ├── AlarmValidationResult - Результат проверки уставок
│   ├── AlarmValidationMessage - Сообщение о проверке уставок
│   ├── DataQualityScore - Оценка качества данных (0-100)
│   ├── ValidationTimestamp - Время последней валидации
│   ├── ValidationErrorCount - Количество ошибок валидации
│   ├── ValidationWarningCount - Количество предупреждений
│   └── FullIdentifier - Полный идентификатор точки
│
├── 🔹 ПОЛЯ ИМПОРТА/ЭКСПОРТА
│   ├── ImportSource - Источник импорта (имя файла)
│   ├── ImportRowNumber - Номер строки в Excel
│   ├── IsImportValid - Флаг валидности импорта
│   ├── ExcelValidationResult - Результат проверки Excel
│   ├── AccessValidationResult - Результат проверки Access
│   ├── AccessConnectionString - Строка подключения Access
│   ├── LastAccessImport - Время последнего импорта из Access
│   ├── LastAccessExport - Время последнего экспорта в Access
│   ├── AccessImportStatus - Статус импорта Access
│   ├── AccessExportStatus - Статус экспорта Access
│   └── IsAccessConnected - Флаг подключения к Access
│
├── 🔹 ИНТЕРФЕЙСЫ (Interfaces)
│   ├── IIOPoint - Базовый интерфейс для всех точек
│   ├── IIdentifiable - Идентификация объектов
│   ├── IValidatable - Валидация данных
│   ├── IExcelImportValidator - Валидация данных из Excel
│   ├── IAccessImportValidator - Валидация данных из Access
│   ├── IBatchValidator - Пакетная валидация
│   ├── IVersionable - Управление версиями
│   ├── ICloneable<T> - Клонирование объектов
│   ├── IEquatable<IOPoint> - Сравнение точек
│   ├── IRangeSignal - Работа с диапазонами сигналов
│   ├── IAlarmable - Работа с аварийной сигнализацией
│   ├── IAlarmValidator - Валидация уставок
│   ├── ICableConnection - Управление кабельными соединениями
│   ├── IRepository<T> - Базовый репозиторий
│   ├── IIOPointRepository - Репозиторий для IOPoint
│   ├── IUnitOfWork - Единица работы
│   ├── IDataService - Сервис данных
│   ├── IAccessDataProvider - Провайдер данных Access
│   └── IImportExportService - Сервис импорта/экспорта
│
├── 🔹 СОБЫТИЯ (Events)
│   ├── ValueChanged - Изменение значения сигнала
│   ├── AlarmTriggered - Срабатывание аварии
│   ├── AlarmValidated - Проверка уставок
│   ├── AlarmValidationError - Ошибка в иерархии уставок
│   ├── ExcelValidationStarted - Начало проверки Excel
│   ├── ExcelValidationCompleted - Завершение проверки Excel
│   ├── ExcelValidationError - Ошибка проверки Excel
│   ├── ExcelValidationWarning - Предупреждение проверки Excel
│   ├── AccessValidationStarted - Начало проверки Access
│   ├── AccessValidationCompleted - Завершение проверки Access
│   ├── AccessValidationError - Ошибка проверки Access
│   ├── AccessValidationWarning - Предупреждение проверки Access
│   ├── AccessImportStarted - Начало импорта из Access
│   ├── AccessImportCompleted - Завершение импорта из Access
│   ├── AccessExportStarted - Начало экспорта в Access
│   ├── AccessExportCompleted - Завершение экспорта в Access
│   ├── AccessConnectionChanged - Изменение подключения к Access
│   ├── RangeExceeded - Выход за пределы диапазона
│   ├── StatusChanged - Изменение статуса точки
│   ├── RevisionChanged - Обновление ревизии
│   ├── TagChanged - Изменение Tag
│   ├── TagValidationError - Ошибка валидации Tag
│   ├── DataQualityChanged - Изменение качества данных
│   ├── ValidationError - Ошибка валидации
│   ├── PointCreated - Создание точки
│   ├── PointUpdated - Обновление точки
│   ├── PointDeleted - Удаление точки
│   └── PointRestored - Восстановление точки
│
├── 🔹 ОПЕРАТОРЫ (Operators)
│   ├── == / != (IOPoint, IOPoint) - Сравнение точек
│   ├── == / != (IOPoint, string) - Сравнение с тегом
│   ├── == / != (string, IOPoint) - Сравнение тега с точкой
│   ├── implicit operator string - Неявное преобразование в строку
│   ├── explicit operator IOPoint - Явное преобразование из строки
│   └── + (IOPoint, IOPoint) - Объединение данных двух точек
│
├── 🔹 ИНДЕКСАТОРЫ (Indexers)
│   ├── this[string propertyName] - Доступ к свойствам по имени
│   └── this[string tag, bool useCache] - Поиск по тегу с кэшированием
│
└── 🔹 ДЕЛЕГАТЫ (Delegates)
    ├── ValueChangedHandler - Изменение значения
    ├── AlarmHandler - Аварийные события
    ├── AlarmValidationHandler - Проверка уставок
    ├── ExcelValidationHandler - Проверка Excel
    ├── ExcelErrorHandler - Ошибки Excel
    ├── AccessValidationHandler - Проверка Access
    ├── AccessErrorHandler - Ошибки Access
    ├── AccessImportHandler - Импорт из Access
    ├── AccessExportHandler - Экспорт в Access
    ├── TagValidationHandler - Валидация Tag
    ├── ValidationHandler - Ошибки валидации
    ├── PointCreatedHandler - Создание точки
    ├── PointUpdatedHandler - Обновление точки
    ├── PointDeletedHandler - Удаление точки
    ├── PointFilter - Фильтр для точек
    ├── PointAction - Действие над точкой
    └── PointTransformer - Трансформатор точки
```

---

## 📋 ДЕТАЛЬНОЕ ОПИСАНИЕ ВСЕХ КОМПОНЕНТОВ

---

### 🔹 ВЛОЖЕННЫЕ КЛАССЫ (Nested Classes)

#### 1. ВАЛИДАТОР EXCEL (ExcelValidator)
**Назначение:** Проверка данных, импортируемых из Excel

| Метод | Описание |
|:---|:---|
| ValidateRow | Проверка отдельной строки Excel |
| ValidateStructure | Проверка структуры данных |
| ValidateColumns | Проверка наличия обязательных колонок |
| ValidateDataTypes | Проверка типов данных |
| ValidateFormat | Проверка формата данных |
| ValidateRelations | Проверка связей между полями |
| ValidateMandatoryFields | Проверка обязательных полей |
| GetValidationResult | Комплексная проверка |
| GetValidationSummary | Сводка по валидации |

---

#### 2. ПРОВАЙДЕР ДАННЫХ ACCESS (AccessDataProvider)
**Назначение:** Управление подключением и операциями с базой данных Access

| Метод | Описание |
|:---|:---|
| Connect | Подключение к базе Access |
| Disconnect | Отключение от базы |
| IsConnected | Проверка подключения |
| GetConnectionString | Получение строки подключения |
| TestConnection | Проверка соединения |
| GetDatabaseInfo | Информация о базе |
| GetTableNames | Получение списка таблиц |
| GetTableSchema | Получение схемы таблицы |
| GetTableData | Получение данных таблицы |
| ExecuteQuery | Выполнение запроса |
| ExecuteNonQuery | Выполнение команды без возврата |
| BeginTransaction | Начало транзакции |
| CommitTransaction | Фиксация транзакции |
| RollbackTransaction | Откат транзакции |
| GetTableCount | Подсчет записей в таблице |

---

#### 3. ИМПОРТЕР ИЗ ACCESS (AccessImporter)
**Назначение:** Импорт данных из базы Access

| Метод | Описание |
|:---|:---|
| ImportFromAccess | Импорт из базы Access |
| ImportFromAccessAsync | Асинхронный импорт |
| ImportTable | Импорт таблицы |
| ImportQuery | Импорт результатов запроса |
| ImportWithValidation | Импорт с валидацией |
| ImportIncremental | Инкрементальный импорт |
| ImportFiltered | Импорт с фильтром |
| GetImportPreview | Предварительный просмотр |
| GetImportStatistics | Статистика импорта |
| ValidateBeforeImport | Проверка перед импортом |

---

#### 4. ЭКСПОРТЕР В ACCESS (AccessExporter)
**Назначение:** Экспорт данных в базу Access

| Метод | Описание |
|:---|:---|
| ExportToAccess | Экспорт в базу Access |
| ExportToAccessAsync | Асинхронный экспорт |
| ExportToTable | Экспорт в таблицу |
| ExportToNewTable | Экспорт в новую таблицу |
| ExportWithSchema | Экспорт со схемой |
| ExportWithRelations | Экспорт с связями |
| ExportIncremental | Инкрементальный экспорт |
| ExportFiltered | Экспорт с фильтром |
| GetExportPreview | Предварительный просмотр |
| GetExportStatistics | Статистика экспорта |

---

#### 5. ВАЛИДАТОР ACCESS (AccessValidator)
**Назначение:** Проверка данных и структуры базы Access

| Метод | Описание |
|:---|:---|
| ValidateTableStructure | Проверка структуры таблицы |
| ValidateDataTypes | Проверка типов данных |
| ValidateRelations | Проверка связей |
| ValidateConstraints | Проверка ограничений |
| ValidateIndexes | Проверка индексов |
| ValidateData | Проверка данных |
| ValidateCompatibility | Проверка совместимости |
| ValidateIntegrity | Проверка целостности |
| ValidateBeforeImport | Проверка перед импортом |

---

#### 6. РЕПОЗИТОРИЙ ACCESS (AccessRepository)
**Назначение:** Специализированные операции с точками в Access

| Метод | Описание |
|:---|:---|
| GetPoints | Получение всех точек |
| GetPointByTag | Получение по тегу |
| GetPointsByArea | Получение по зоне |
| GetPointsBySystem | Получение по системе |
| GetPointsByStatus | Получение по статусу |
| AddPoint | Добавление точки |
| UpdatePoint | Обновление точки |
| DeletePoint | Удаление точки |
| GetStatistics | Получение статистики |
| CreateTable | Создание таблицы |
| DropTable | Удаление таблицы |
| CreateIndex | Создание индекса |
| ExecuteScript | Выполнение скрипта |
| GetSchemaInfo | Получение информации о схеме |

---

#### 7. ВАЛИДАТОР УСТАВОК (AlarmValidator)
**Назначение:** Проверка корректности иерархии уставок LL2 < LL < L < H < HH < HH2

| Метод | Описание |
|:---|:---|
| ValidateHierarchy | Проверка иерархии LL2 < LL < L < H < HH < HH2 |
| ValidateConsistency | Проверка согласованности уставок |
| ValidateUnits | Проверка единиц измерения |
| ValidateWithRange | Проверка относительно диапазона |
| GetValidationResult | Комплексная проверка |
| AutoFixHierarchy | Автоматическое исправление |

---

#### 8. УПРАВЛЕНИЕ TAG (TagManager)
**Назначение:** Централизованное управление уникальностью и валидацией Tag

| Метод | Описание |
|:---|:---|
| ValidateTag | Проверка Tag на уникальность и соответствие формату |
| GenerateTag | Генерация уникального Tag на основе базового |
| RegisterTag | Регистрация Tag в глобальном реестре |
| UnregisterTag | Удаление Tag из глобального реестра |
| IsTagRegistered | Проверка регистрации Tag |
| NormalizeTag | Приведение Tag к стандартному формату |
| CompareTags | Сравнение Tag с учетом регистра |
| GetTagValidationRules | Получение правил валидации Tag |

---

#### 9. ИСТОРИЯ ИЗМЕНЕНИЙ (RevisionHistory)
**Назначение:** Хранение и управление историей версий точки

| Свойство/Метод | Описание |
|:---|:---|
| Entries | Список записей истории |
| Add | Добавление записи в историю |
| GetLastEntry | Получение последней записи |
| GetEntriesByAuthor | Получение записей по автору |
| RevisionEntry | Запись в истории (вложенный класс) |
| RevisionEntry.Revision | Ревизия |
| RevisionEntry.Author | Автор |
| RevisionEntry.Timestamp | Время изменения |

---

#### 10. МОНИТОРИНГ СОСТОЯНИЯ (Monitor)
**Назначение:** Автоматический мониторинг состояния точки с заданным интервалом

| Свойство/Метод | Описание |
|:---|:---|
| IsRunning | Флаг работы мониторинга |
| MonitoringInterval | Интервал мониторинга |
| StartMonitoring | Запуск мониторинга |
| StopMonitoring | Остановка мониторинга |
| CheckStatus | Проверка состояния (внутренний метод) |

---

#### 11. ССЫЛКА НА P&ID (PidReference)
**Назначение:** Привязка точки к технологической схеме P&ID

| Свойство/Метод | Описание |
|:---|:---|
| PidNumber | Номер P&ID |
| SheetNumber | Номер листа |
| Description | Описание |
| Position | Координаты на схеме |
| ConnectedTo | Список связанных элементов |
| GetPidUrl | Получение URL на P&ID |
| Point | Координаты (вложенная структура) |
| Point.X | Координата X |
| Point.Y | Координата Y |

---

#### 12. РЕЗУЛЬТАТ ВАЛИДАЦИИ EXCEL (ExcelValidationResult)
**Назначение:** Хранение результата проверки данных из Excel

| Свойство | Описание |
|:---|:---|
| IsValid | Флаг валидности всех данных |
| Errors | Список критических ошибок |
| Warnings | Список предупреждений |
| Infos | Информационные сообщения |
| ValidatedAt | Время проверки |
| TotalRows | Всего строк в файле |
| ValidRows | Валидных строк |
| InvalidRows | Невалидных строк |
| DataQualityScore | Оценка качества данных (0-100) |
| ValidationSummary | Сводка валидации |

---

#### 13. РЕЗУЛЬТАТ ВАЛИДАЦИИ ACCESS (AccessValidationResult)
**Назначение:** Хранение результата проверки данных из Access

| Свойство | Описание |
|:---|:---|
| IsValid | Флаг валидности всех данных |
| Errors | Список ошибок |
| Warnings | Список предупреждений |
| ValidatedAt | Время проверки |
| TotalRecords | Всего записей |
| ValidRecords | Валидных записей |
| InvalidRecords | Невалидных записей |
| DataQualityScore | Оценка качества данных |
| ValidationSummary | Сводка валидации |

---

#### 14. ОТЧЕТ О КАЧЕСТВЕ ДАННЫХ (DataQualityReport)
**Назначение:** Отчет о качестве данных точки

| Свойство | Описание |
|:---|:---|
| OverallScore | Общая оценка качества (0-100) |
| Completeness | Полнота данных (%) |
| Accuracy | Точность данных (%) |
| Consistency | Согласованность данных (%) |
| MissingFields | Отсутствующие поля |
| InvalidFields | Некорректные поля |
| CriticalIssues | Критические проблемы |
| Warnings | Предупреждения |
| Recommendations | Рекомендации |
| GeneratedAt | Время создания |

---

#### 15. ДАННЫЕ СТРОКИ EXCEL (ExcelRowData)
**Назначение:** Представление данных строки Excel

| Свойство | Описание |
|:---|:---|
| RowNumber | Номер строки в Excel |
| Columns | Данные колонок (имя колонки → значение) |
| SourceFile | Имя файла источника |
| RawData | Сырые данные строки |

---

#### 16. ДАННЫЕ СТРОКИ ACCESS (AccessRowData)
**Назначение:** Представление данных строки из Access

| Свойство | Описание |
|:---|:---|
| RowNumber | Номер строки |
| Columns | Данные колонок |
| TableName | Имя таблицы |
| SourceFile | Имя файла базы |
| ConnectionString | Строка подключения |

---

#### 17. ОШИБКА ВАЛИДАЦИИ (ValidationError)
**Назначение:** Информация об ошибках при проверке данных

| Свойство | Описание |
|:---|:---|
| Property | Свойство с ошибкой |
| Message | Сообщение об ошибке |
| InvalidValue | Некорректное значение |
| Timestamp | Время возникновения |

---

#### 18. РЕЗУЛЬТАТ ПРОВЕРКИ УСТАВОК (AlarmValidationResult)
**Назначение:** Хранение результата проверки уставок

| Свойство | Описание |
|:---|:---|
| IsValid | Флаг корректности уставок |
| Errors | Список критических ошибок |
| Warnings | Список предупреждений |
| SuggestedFixes | Предложения по исправлению |
| ValidatedAt | Время проверки |
| Severity | Степень серьезности |

---

#### 19. ПОСТРОИТЕЛЬ ФИЛЬТРОВ (PointFilterBuilder)
**Назначение:** Построение сложных фильтров для поиска точек

| Метод | Описание |
|:---|:---|
| AddAreaFilter | Добавление фильтра по зоне |
| AddSystemFilter | Добавление фильтра по системе |
| AddStatusFilter | Добавление фильтра по статусу |
| AddServiceFilter | Добавление фильтра по описанию |
| AddTagFilter | Добавление фильтра по тегу |
| AddDataQualityFilter | Добавление фильтра по качеству |
| Build | Построение готового фильтра |

---

#### 20. РЕЗУЛЬТАТ С ПАГИНАЦИЕЙ (PagedResult<T>)
**Назначение:** Результат запроса с пагинацией

| Свойство | Описание |
|:---|:---|
| Items | Элементы текущей страницы |
| TotalCount | Общее количество записей |
| PageNumber | Номер текущей страницы |
| PageSize | Размер страницы |
| TotalPages | Общее количество страниц |
| HasPreviousPage | Флаг наличия предыдущей страницы |
| HasNextPage | Флаг наличия следующей страницы |

---

#### 21. КРИТЕРИИ ПОИСКА (SearchCriteria)
**Назначение:** Критерии для расширенного поиска точек

| Свойство | Описание |
|:---|:---|
| SearchTerm | Поисковый запрос |
| Area | Фильтр по зоне |
| System | Фильтр по системе |
| Location | Фильтр по месту установки |
| Status | Фильтр по статусу |
| DataQualityMin | Минимальное качество данных |
| DateFrom | Начальная дата |
| DateTo | Конечная дата |

---

#### 22. РЕЗУЛЬТАТ ИМПОРТА (ImportResult)
**Назначение:** Результат импорта данных

| Свойство | Описание |
|:---|:---|
| ImportedCount | Количество импортированных записей |
| UpdatedCount | Количество обновленных записей |
| ErrorCount | Количество ошибок |
| WarningCount | Количество предупреждений |
| Errors | Список ошибок |
| Warnings | Список предупреждений |
| ImportedPoints | Импортированные точки |
| SourceFile | Имя файла источника |
| Duration | Длительность импорта |

---

#### 23. РЕЗУЛЬТАТ ЭКСПОРТА В ACCESS (AccessExportResult)
**Назначение:** Результат экспорта в Access

| Свойство | Описание |
|:---|:---|
| ExportedCount | Количество экспортированных записей |
| UpdatedCount | Количество обновленных записей |
| ErrorCount | Количество ошибок |
| WarningCount | Количество предупреждений |
| TableName | Имя таблицы |
| Duration | Длительность экспорта |

---

### 🔹 ПЕРЕЧИСЛЕНИЯ (Enums)

#### SystemType
**Назначение:** Тип системы управления

| Значение | Описание |
|:---|:---|
| DCS | Распределенная система управления |
| SCS | Система безопасности / SIS |
| GDS | Система обнаружения газа |

#### LocationType
**Назначение:** Место установки оборудования

| Значение | Описание |
|:---|:---|
| Field | Полевое оборудование |
| SIS | Система безопасности |
| MCC | Шкаф мотор-контроллеров |
| PLC | Программируемый логический контроллер |
| AUX | Вспомогательное оборудование |
| GDS | Система обнаружения газа |

#### ExProtectionType
**Назначение:** Тип взрывозащиты

| Значение | Описание |
|:---|:---|
| ExD | Взрывонепроницаемая оболочка |
| ExI | Искробезопасная цепь |
| ExE | Повышенная защита |
| ExN | Неискрящее оборудование |
| ExP | Заполнение под давлением |
| ExO | Масляное заполнение |
| ExQ | Кварцевое заполнение |
| ExM | Герметизация компаундом |
| None | Взрывозащита не требуется |

#### IOPointStatus
**Назначение:** Статус точки в системе

| Значение | Описание |
|:---|:---|
| Active | Активен (штатный режим) |
| Inactive | Неактивен (отключен) |
| Commissioning | Ввод в эксплуатацию |
| Maintenance | На обслуживании |
| Fault | Ошибка (неисправность) |
| Decommissioned | Выведен из эксплуатации |

#### AlarmValidationSeverity
**Назначение:** Степень серьезности при проверке уставок

| Значение | Описание |
|:---|:---|
| Critical | Критическая ошибка |
| Error | Ошибка |
| Warning | Предупреждение |
| Info | Информация |
| Valid | Корректно |

#### AccessImportStatus
**Назначение:** Статус импорта из Access

| Значение | Описание |
|:---|:---|
| NotStarted | Не начат |
| InProgress | В процессе |
| Completed | Завершен |
| Failed | Ошибка |
| Cancelled | Отменен |

#### AccessExportStatus
**Назначение:** Статус экспорта в Access

| Значение | Описание |
|:---|:---|
| NotStarted | Не начат |
| InProgress | В процессе |
| Completed | Завершен |
| Failed | Ошибка |
| Cancelled | Отменен |

---

### 🔹 СТАТИЧЕСКИЕ КОНСТАНТЫ

| Константа | Значение | Описание |
|:---|:---|:---|
| MaxAreaLength | 6 | Максимальная длина Area (1-6 цифр) |
| MinTagNumberLength | 1 | Минимальная длина TagNumber |
| MaxTagNumberLength | 3 | Максимальная длина TagNumber (1-3 цифры) |
| MaxDeviceClassLength | 5 | Максимальная длина DeviceClass (1-5 букв) |
| MinTagLength | 3 | Минимальная длина Tag |
| MaxTagLength | 50 | Максимальная длина Tag |
| TagPattern | ^[A-Z0-9-]+$ | Шаблон для валидации Tag |
| AlarmHierarchy | LL2 < LL < L < H < HH < HH2 | Правило иерархии уставок |
| DefaultUnit | % | Единица измерения по умолчанию |
| MaxImportFileSizeMB | 100 | Максимальный размер файла импорта |
| SupportedExcelFormats | .xlsx, .xls | Поддерживаемые форматы Excel |
| SupportedAccessFormats | .accdb, .mdb | Поддерживаемые форматы Access |
| DefaultPageSize | 50 | Размер страницы по умолчанию |
| MaxPageSize | 1000 | Максимальный размер страницы |

---

### 🔹 МЕТОДЫ РАСШИРЕНИЯ (Extension Methods)

#### Фильтрация
| Метод | Описание |
|:---|:---|
| FilterBySystem | Фильтрация по системе управления |
| FilterByLocation | Фильтрация по месту установки |
| FilterByStatus | Фильтрация по статусу |
| FilterByArea | Фильтрация по зоне |
| FilterByService | Фильтрация по описанию |
| FilterByValidAlarms | Фильтрация по корректности уставок |
| FilterByDataQuality | Фильтрация по качеству данных |
| FilterByValidationStatus | Фильтрация по статусу валидации |
| FilterByImportSource | Фильтрация по источнику импорта |
| FilterByDateRange | Фильтрация по диапазону дат |
| FilterByController | Фильтрация по контроллеру |
| FilterByIds | Фильтрация по списку идентификаторов |
| FilterByAccessSource | Фильтрация по источнику Access |
| FilterByAccessStatus | Фильтрация по статусу Access |

#### Группировка
| Метод | Описание |
|:---|:---|
| GroupBySystem | Группировка по системе управления |
| GroupByArea | Группировка по зоне |
| GroupByStatus | Группировка по статусу |
| GroupByAlarmValidity | Группировка по корректности уставок |
| GroupByDataQualityLevel | Группировка по уровню качества |
| GroupByValidationStatus | Группировка по статусу валидации |
| GroupByAccessSource | Группировка по источнику Access |
| GroupByAccessStatus | Группировка по статусу Access |

#### Поиск
| Метод | Описание |
|:---|:---|
| FindByTag | Поиск по уникальному тегу |
| FindAllByService | Поиск по описанию |
| FindByTagPattern | Поиск по шаблону Tag |
| FindWithInvalidAlarms | Поиск с некорректными уставками |
| FindWithDataQualityIssues | Поиск с проблемами качества |
| FindByImportSource | Поиск по источнику импорта |
| FindByIds | Поиск по списку идентификаторов |
| FindByDateRange | Поиск по диапазону дат |
| FindByAccessSource | Поиск по источнику Access |
| FindByAccessStatus | Поиск по статусу Access |

#### Сортировка
| Метод | Описание |
|:---|:---|
| SortByTag | Сортировка по тегу |
| SortByArea | Сортировка по зоне |
| SortByDataQuality | Сортировка по качеству данных |
| SortByCreatedAt | Сортировка по дате создания |
| SortByModifiedAt | Сортировка по дате изменения |
| SortByStatus | Сортировка по статусу |
| SortByAccessSource | Сортировка по источнику Access |
| SortByAccessImportDate | Сортировка по дате импорта Access |

#### Пагинация
| Метод | Описание |
|:---|:---|
| ToPagedResult | Преобразование в результат с пагинацией |
| GetPage | Получение страницы |
| GetFirstPage | Получение первой страницы |
| GetLastPage | Получение последней страницы |
| GetNextPage | Получение следующей страницы |
| GetPreviousPage | Получение предыдущей страницы |

#### Экспорт
| Метод | Описание |
|:---|:---|
| ExportToCsv | Экспорт в CSV формат |
| ExportToJson | Экспорт в JSON формат |
| ExportToExcel | Экспорт в Excel формат |
| ExportToAccess | Экспорт в Access формат |
| ExportAlarmValidationReport | Экспорт отчета по уставкам |
| ExportDataQualityReport | Экспорт отчета по качеству |
| ExportValidationReport | Экспорт отчета по валидации |
| ExportErrorReport | Экспорт отчета по ошибкам |
| ExportAccessReport | Экспорт отчета по Access |

#### Статистика
| Метод | Описание |
|:---|:---|
| CountBySystem | Подсчет по системе |
| GetSystemStatistics | Статистика по системам |
| GetTagStatistics | Статистика по Tag |
| GetAlarmStatistics | Статистика по уставкам |
| GetDataQualityStatistics | Статистика качества |
| GetAccessStatistics | Статистика Access |
| GetAccessImportExportStatistics | Статистика импорта/экспорта Access |

#### Пакетные операции
| Метод | Описание |
|:---|:---|
| BatchUpdate | Параллельное обновление |
| ForEachPoint | Последовательная обработка |
| ValidateAllAlarms | Проверка уставок всех точек |
| FixAllAlarmHierarchies | Исправление всех уставок |
| ValidateAllExcelImports | Проверка всех импортов |
| ValidateAllAccessImports | Проверка всех импортов Access |
| ExportAllToAccess | Экспорт всех в Access |
| ImportAllFromAccess | Импорт всех из Access |
| CalculateAllDataQuality | Расчет качества всех точек |

---

### 🔹 СТАТИЧЕСКИЕ МЕТОДЫ

| Метод | Описание |
|:---|:---|
| CreateNew | Создание новой точки |
| CreateFromParsedTag | Создание из распарсенного тега |
| FromCsvRow | Создание из строки CSV |
| FromJson | Создание из JSON |
| FromAccessRow | Создание из строки Access |
| ImportFromExcel | Импорт из Excel |
| ImportFromExcelAsync | Асинхронный импорт из Excel |
| ImportFromAccess | Импорт из Access |
| ImportFromAccessAsync | Асинхронный импорт из Access |
| ExportToAccess | Экспорт в Access |
| ExportToAccessAsync | Асинхронный экспорт в Access |
| GetRepository | Получение репозитория |
| RegisterRepository | Регистрация репозитория |
| GetService | Получение сервиса |
| RegisterService | Регистрация сервиса |
| CreateUnitOfWork | Создание единицы работы |
| BeginTransaction | Начало транзакции |
| CommitTransaction | Фиксация транзакции |
| RollbackTransaction | Откат транзакции |
| RegisterTag | Регистрация Tag |
| UnregisterTag | Удаление Tag |
| IsTagRegistered | Проверка регистрации Tag |
| ValidateTagUniqueness | Проверка уникальности Tag |
| GenerateUniqueTag | Генерация уникального Tag |
| GetCached | Получение из кэша |
| CachePoint | Добавление в кэш |
| ClearCache | Очистка кэша |
| InvalidateCache | Инвалидация кэша |
| GetCacheStats | Статистика кэша |

---

## 📊 СВОДНАЯ ТАБЛИЦА ВСЕХ КОМПОНЕНТОВ

| Категория | Компонент | Количество |
|:---|:---|:---|
| **Основные классы** | IOPoint, Identification, InstrumentTag, Signal, Ranges, Range, AlarmSet, AlarmLevel, Cable, Equipment, Chassis, Module, Revision | 13 |
| **Вложенные классы** | ExcelValidator, AccessDataProvider, AccessImporter, AccessExporter, AccessValidator, AccessRepository, AlarmValidator, TagManager, RevisionHistory, Monitor, PidReference, DataQualityReport, ValidationError, PointFilterBuilder, PagedResult, SearchCriteria, ImportResult, ExcelValidationResult, AccessValidationResult, AlarmValidationResult, ExcelRowData, AccessRowData, AccessImportResult, AccessExportResult | 24 |
| **Интерфейсы** | IIOPoint, IIdentifiable, IValidatable, IExcelImportValidator, IAccessImportValidator, IBatchValidator, IVersionable, ICloneable, IEquatable, IRangeSignal, IAlarmable, IAlarmValidator, ICableConnection, IRepository, IIOPointRepository, IUnitOfWork, IDataService, IAccessDataProvider, IImportExportService | 19 |
| **События** | ValueChanged, AlarmTriggered, AlarmValidated, AlarmValidationError, ExcelValidationStarted, ExcelValidationCompleted, ExcelValidationError, ExcelValidationWarning, AccessValidationStarted, AccessValidationCompleted, AccessValidationError, AccessValidationWarning, AccessImportStarted, AccessImportCompleted, AccessExportStarted, AccessExportCompleted, AccessConnectionChanged, RangeExceeded, StatusChanged, RevisionChanged, TagChanged, TagValidationError, DataQualityChanged, ValidationError, PointCreated, PointUpdated, PointDeleted, PointRestored | 28 |
| **Операторы** | ==, != (3 варианта), implicit, explicit, + | 6 |
| **Индексаторы** | this[string propertyName], this[string tag, bool useCache] | 2 |
| **Делегаты** | ValueChangedHandler, AlarmHandler, AlarmValidationHandler, ExcelValidationHandler, ExcelErrorHandler, AccessValidationHandler, AccessErrorHandler, AccessImportHandler, AccessExportHandler, TagValidationHandler, ValidationHandler, PointCreatedHandler, PointUpdatedHandler, PointDeletedHandler, PointFilter, PointAction, PointTransformer | 17 |
| **Перечисления** | SystemType, LocationType, ExProtectionType, IOPointStatus, AlarmValidationSeverity, AccessImportStatus, AccessExportStatus | 7 |
| **Методы расширения** | Фильтрация (14), Группировка (8), Поиск (10), Сортировка (8), Пагинация (6), Экспорт (9), Статистика (7), Пакетные операции (9) | 71 |
| **Статические константы** | MaxAreaLength, MinTagNumberLength, MaxTagNumberLength, MaxDeviceClassLength, MinTagLength, MaxTagLength, TagPattern, AlarmHierarchy, DefaultUnit, MaxImportFileSizeMB, SupportedExcelFormats, SupportedAccessFormats, DefaultPageSize, MaxPageSize | 14 |
| **Статические методы** | Фабричные (8), Access (15), Валидация Excel (7), Качество (6), Уставки (6), Tag (6), Репозитории (8), Кэширование (6) | 62 |
| **Свойства IOPoint** | Идентификация (13), Сигнал (9), Диапазоны (3 + 12 подсвойств), Кабель (13), Оборудование (7 + 8 подсвойств), Ревизия (11), Системные (19), Импорт/Экспорт (11) | 106 |

---

## ✅ КЛЮЧЕВЫЕ ОСОБЕННОСТИ СТРУКТУРЫ

### 1. Полнота данных
- Все свойства Nullable - гибкость при работе с неполными данными
- Многоуровневая структура для всех аспектов точки ввода-вывода
- Поддержка всех типов систем управления (DCS, SCS, GDS)

### 2. Уникальность Tag
- TagManager для управления уникальностью
- Реестр всех используемых Tag
- Автоматическая генерация при конфликтах
- Валидация формата и уникальности

### 3. Валидация данных
- ExcelValidator для проверки импортируемых данных
- AccessValidator для проверки данных из Access
- AlarmValidator для проверки иерархии уставок
- Детальные результаты с ошибками и предупреждениями

### 4. Работа с уставками
- Проверка иерархии LL2 < LL < L < H < HH < HH2
- Автоматическое исправление нарушений
- Поддержка индивидуальных единиц измерения
- Детальный отчет по проверке

### 5. Импорт/Экспорт данных
- **Excel**: полный цикл импорта/экспорта с валидацией
- **Access**: поддержка .accdb и .mdb форматов
- **CSV и JSON**: дополнительные форматы экспорта
- Инкрементальный импорт и экспорт

### 6. Репозиторный слой
- Базовый репозиторий для CRUD операций
- Специализированный репозиторий для IOPoint
- Кэширующий репозиторий для оптимизации
- Пул подключений для Access

### 7. Сервисный слой
- Бизнес-логика для работы с точками
- Пакетная обработка данных
- Генерация отчетов и аналитика
- Управление транзакциями

### 8. Событийная модель
- Полный набор событий для всех операций
- Оповещение об изменениях в реальном времени
- Обработка ошибок и предупреждений
- Интеграция с внешними системами

### 9. Расширяемость
- Методы расширения для работы с коллекциями
- Интерфейсы для всех компонентов
- Гибкая система фильтрации и поиска
- Поддержка различных форматов данных

### 10. Контроль качества
- Оценка качества данных (0-100)
- Отслеживание полноты и точности данных
- Автоматическое обнаружение проблем
- Рекомендации по улучшению